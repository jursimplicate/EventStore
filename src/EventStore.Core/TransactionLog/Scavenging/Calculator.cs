using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Calculator<TStreamId> : ICalculator<TStreamId> {
		private readonly IIndexReaderForCalculator<TStreamId> _index;

		public Calculator(IIndexReaderForCalculator<TStreamId> index) {
			_index = index;
		}

		public void Calculate(
			ScavengePoint scavengePoint,
			IScavengeStateForCalculator<TStreamId> scavengeState) {

			// iterate through the metadata streams, for each one use the metadata to modify the
			// discard point of the stream and store it. along the way note down which chunks
			// the records to be discarded.
			foreach (var (metastreamHandle, metastreamData) in scavengeState.MetastreamDatas) {
				var originalStreamHandle = GetOriginalStreamHandle(
					metastreamHandle,
					metastreamData.OriginalStreamHash);

				//qq it would be neat if this interface gave us some hint about the location of
				// the DP so that we could set it in a moment cheaply without having to search.
				// although, if its a wal that'll be cheap anyway.
				//
				//qq i dont think we can save this lookup by storing it on the metastreamData
				// because when we find, say, the tombstone of the original stream and want to set its
				// DP, the metadata stream does not necessarily exist.
				// unless we don't care here about the tombstone, and only care about things that could
				// be conveniently set on the metastreamdata by the accumulator
				//qq if the scavengemap supports RMW that might have a bearing too, but for now maybe
				// this is just overcomplicating things.
				//qq how bad is this, how much could we save
				if (!scavengeState.TryGetOriginalStreamData(
						originalStreamHandle,
						out var originalDiscardPoint)) {
					originalDiscardPoint = DiscardPoint.KeepAll;
				}

				var adjustedDiscardPoint = CalculateDiscardPointForStream(
					scavengeState,
					originalStreamHandle,
					originalDiscardPoint,
					metastreamData,
					scavengePoint);

				scavengeState.SetOriginalStreamData(originalStreamHandle, adjustedDiscardPoint);
			}
		}

		//qq this is broken
		//qq implement this, perhaps in another class that can be unit tested separately
		// This gets the handle to the original stream, given the handle to the metadata stream and the
		// hash of the original stream.
		//
		// The resulting handle needs to contain the original stream name if it is a collision,
		// and just the hash if it is not a collision.
		//
		// Consider cases according to whether the metadata stream and the original stream collide with
		// anything:
		//
		// - say neither stram collides with anything
		//     then it is fine, we use the originalstreamhash to look up the lasteventnumber
		//     and, and store the result against the originalstreamhash.
		//
		// - say the meta stream collides (and we are have a handle to it)
		//     fine. basically same as above
		//
		// - say the original stream collides, (but the metastreams dont)
		//     means we will have hash handles to metastreamdata objects with original hashes it
		//       where the original hashes are the same.
		//     123 => originalHash: 5
		//     124 => originalHash: 5
		//     we need to (i) notice that 5 is a collision and (ii) find out the stream name that
		//     hashes to it in each case.
		//
		//     remember we have a (very short) list of all the stream names that collide.
		//     we use that to build a map from hashcollision to list of stream names that map to it.
		//     we can then discover that (5) is a collision of "a" and "b", we can then hash
		//     "$$a" and "$$b" until we discover which maps to 123. that will tell us which
		//     stream (a or b) this metadata is for, and give us a streamnamehandle to manipulate it.
		//
		//     NO this doesn't work if one of the original streams doesn't actually exist, then it wont
		//     collide.
		//
		// - say they both collide. worst case, but actually slightly easier.
		//     then we have:
		//     $$a => originalHash: 5
		//     $$b => originalHash: 5
		//       
		//     we will notice that 5 is a collision, but we will know immediately from the handle
		//     that this is the metadata in stream "$$a" therefore for stream "a"

		private StreamHandle<TStreamId> GetOriginalStreamHandle(
			StreamHandle<TStreamId> metastreamHandle,
			ulong originalStreamHash) {

			//qqqqqqqqqqqqqq needs to check for collision and use the ForStreamName if necessary
			return StreamHandle.ForHash<TStreamId>(originalStreamHash);
		}

		// streamHandle: handle to the original stream
		// discardPoint: the discard point previously set by the accumulator
		// metadata: metadata of the original stream (stored in the metadata stream)
		private DiscardPoint CalculateDiscardPointForStream(
			IScavengeStateForCalculator<TStreamId> scavengeState,
			StreamHandle<TStreamId> streamHandle,
			DiscardPoint discardPoint,
			MetastreamData metadata,
			ScavengePoint scavengePoint) {

			// Events in original streams can be discarded because of:
			//   Tombstones, TruncateBefore, MaxCount, MaxAge.
			// SO:
			// 1. determine an overall discard point from
			//       - a) discard point (this covers Tombstones)
			//       - b) truncate before
			//       - c) maxcount
			// 2. and a logposition cutoff for
			//       - d) maxage   <- this one does require looking at the location of the events
			// 3. iterate through the eventinfos calling discard for each one that is discarded
			//    by the discard point and logpositioncutoff. this discovers the final discard point.
			// 4. return the final discard point.
			//
			// there are, therefore, three discard points to keep clear,
			//qq and which all need better names
			// - the `originalDiscardPoint` determined by the Accumulator without respect to the
			//   scavengepoint (but includes tombstone)
			// - the `modifiedDiscardPoint` which takes into account the maxcount and tb by applying the
			//   scavenge point
			// - the finalDiscardPoint which takes into account the maxage and
			//   ensures not discarding the last event
			//qq        ^ hum.. this last might involve moving the discard point backwards
			//            so there are some points when we do need to move it backwards
			// this method calculates the FinalDiscardPoint

			//qq the stream really should exist but consider what will happen here if it doesn't
			//qqqqqqq erm, why should it exist, what if it just doesn't, or doesn't before the 
			// scavenge point.
			//  if it doesn't exist at all then presumably there is nothing to scavenge
			//    we can set the disard point to anything
			//  if it doesn't exist before the scavenge point but does later then
			//    there is nothing to remove as part of this scavenge, but we need to be careful
			//    not to remove the later events.
			var lastEventNumber = _index.GetLastEventNumber(
				streamHandle,
				scavengePoint);

			var logPositionCutoff = 0L;

			// if we are already discarding the maximum, then no need to bother adjusting it
			// or calculating logPositionCutoff. we just discard everything except the tombstone.
			if (discardPoint != DiscardPoint.Tombstone) {
				//qq check these all carefuly

				// Discard more if required by TruncateBefore
				if (metadata.TruncateBefore is not null) {
					var dpTruncateBefore = DiscardPoint.DiscardBefore(metadata.TruncateBefore.Value);
					discardPoint = DiscardPoint.AnyOf(discardPoint, dpTruncateBefore);
				}

				// Discard more if required by MaxCount
				if (metadata.MaxCount is not null) {
					//qq turn these into tests. although remember overall we will never discard the last
					//event
					// say the lastEventNumber in the stream is number 5
					// and the maxCount is 2
					// then we want to keep events numbered 4 and 5
					// we want to discard events including event 3
					//
					// say the lastEventNumber is 5
					// and the maxCount is 0
					// we want to discard events including event 5
					//
					// say the lastEventNumber is 5
					// and the maxCount is 7
					// we want to discard events including event -2 (keep all events)
					var lastEventToDiscard = lastEventNumber - metadata.MaxCount.Value;
					var dpMaxCount = DiscardPoint.DiscardIncluding(lastEventToDiscard);
					discardPoint = DiscardPoint.AnyOf(discardPoint, dpMaxCount);
				}

				// Discard more if required by MaxAge
				// here we determine the logPositionCutoff, we won't know which event number
				// (DiscardPoint) that will translate into until we start looking at the EventInfos.
				if (metadata.MaxAge is not null) {
					logPositionCutoff = CalculateLogCutoffForMaxAge(
						scavengePoint,
						metadata.MaxAge.Value);
				}
			}

			// Now discardPoint and logPositionCutoff are set. iterate through the EventInfos calling
			// discard for each one that is discarded (so each chunk knows how many records are discarded)
			// This determines the final DiscardPoint, accounting for logPositionCutoff (MaxAge)
			// Note: when the handle is a hash the ReadEventInfoForward call is index-only

			// read in slices because the stream might be huge.
			const int maxCount = 100; //qq what would be sensible? probably pretty large
			var fromEventNumber = 0L; //qq maybe from the previous scavenge point
			while (true) {
				//qq limit the read to the scavengepoint too?
				var slice = _index.ReadEventInfoForward(
					streamHandle,
					fromEventNumber,
					maxCount,
					scavengePoint);

				//qq naive, we dont need to check every event, we could check the last one
				// and if that is to be discarded then we can discard everything in this slice.
				foreach (var eventInfo in slice) {
					//qq consider this inequality
					var isLastEventInStream = eventInfo.EventNumber == lastEventNumber;
					var beforeScavengePoint = eventInfo.LogPosition < scavengePoint.Position;
					//qq correct inequality?
					var discardForLogPosition = eventInfo.LogPosition < logPositionCutoff;
					var discardForEventNumber = discardPoint.ShouldDiscard(eventInfo.EventNumber);

					var discard =
						!isLastEventInStream &&
						beforeScavengePoint &&
						(discardForLogPosition || discardForEventNumber);

					// always keep the last event in the stream.
					if (discard) {
						Discard(scavengeState, eventInfo.LogPosition);
					} else {
						// found the first one to keep. we are done discarding.
						return DiscardPoint.DiscardBefore(eventInfo.EventNumber);
					}
				}

				if (slice.Length < maxCount) {
					//qq we discarded everything in the stream, this should never happen
					// since we always keep the last event (..unless ignore hard deletes
					// is enabled)
					// which ones are otherwise in danger of removing all the events?
					//  hard deleted?
					//
					//qq although, the old scavenge might be capable of removing all the events
					// after this scavenge point... which would produce this condition.
					//
					// in these situatiosn what discard point should we return, or do we need to abort
					throw new Exception("panic"); //qq dont panic really shouldn't
				}

				fromEventNumber += slice.Length;
			}
		}

		//qq rename.
		//qq fill this in using IsExpiredByMaxAge below
		// it wants to return the log position of a record (probably at the start of a chunk)
		// such that the event at that position is older than the maxage, allowing for some skew
		// so that we are pretty sure that any events before that position would also be excluded
		// by that maxage.
		static long CalculateLogCutoffForMaxAge(
			ScavengePoint scavengePoint,
			TimeSpan maxAge) {

			// binary chop or maybe just scan backwards the chunk starting at times.
			throw new NotImplementedException();
		}

		//qq nb: index-only shortcut for maxage works for transactions too because it is the
		// prepare timestamp that we use not the commit timestamp.
		//qq replace this with CalculateLogCutoffForMaxAge above
		static bool IsExpiredByMaxAge(
			ScavengePoint scavengePoint,
			MetastreamData streamData,
			long logPosition) {

			if (streamData.MaxAge == null)
				return false;

			//qq a couple of these methods calculate the chunk number, consider passing
			// the chunk number in directly
			var chunkNumber = (int)(logPosition / TFConsts.ChunkSize);

			// We can discard the event when it is as old or older than the cutoff
			var cutoff = scavengePoint.EffectiveNow - streamData.MaxAge.Value;

			// but we weaken the condition to say we only discard when the whole chunk is older than
			// the cutoff (which implies that the event certainly is)
			// the whole chunk is older than the cutoff only when the next chunk started before the
			// cutoff
			var nextChunkCreatedAt = GetChunkCreatedAt(chunkNumber + 1);
			//qq ^ consider if there might not be a next chunk
			// say we closed the last chunk (this one) exactly on a boundary and haven't created the next
			// one yet.

			// however, consider clock skew. we want to avoid the case where we accidentally discard a
			// record that we should have kept, because the chunk stamp said discard but the real record
			// stamp would have said keep.
			// for this to happen the records stamp would have to be newer than the chunk stamp.
			// add a maxSkew to the nextChunkCreatedAt to make it discard less.
			//qq make configurable
			var nextChunkCreatedAtIncludingSkew = nextChunkCreatedAt + TimeSpan.FromMinutes(1);
			var discard = nextChunkCreatedAtIncludingSkew <= cutoff;
			return discard;

			DateTime GetChunkCreatedAt(int chunkNumber) {
				throw new NotImplementedException(); //qq
			}
		}

		// figure out which chunk it is for and note it down
		//qq chunk instructions are per logical chunk (for now)
		private void Discard(
			IScavengeStateForCalculator<TStreamId> scavengeState,
			long logPosition) {

			var chunkNumber = (int)(logPosition / TFConsts.ChunkSize);

			//qq dont go lookin it up every time, hold on to one set of chunkinstructions until we
			// have made it to the next chunk.
			if (!scavengeState.TryGetHeuristic(chunkNumber, out var heuristic))
				heuristic = 0;
			scavengeState.SetHeuristic(chunkNumber, heuristic++);
		}
	}
}
