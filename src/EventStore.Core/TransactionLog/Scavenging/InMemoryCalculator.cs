using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class InMemoryCalculator<TStreamId> : ICalculator<TStreamId> {
		private readonly IIndexForScavenge<TStreamId> _index;
		private readonly Dictionary<int, IChunkScavengeInstructions<TStreamId>> _instructionsByChunk =
			new();

		public InMemoryCalculator(IIndexForScavenge<TStreamId> index) {
			_index = index;
		}

		// determine the discard point for each scavengeable stream
		//
		// scavengeable streams are the streams that
		//  - have metadata.
		//  - are metadata streams. <- accumulator already calculated the discard point for these
		//  - have tombstones. <- accumulator already calculated the discard points for these
		//
		//qq here is the key difference between the accumulator and the calculator: the accumulator picks
		// up the criteria for what to scavenge, but does not decide all of what to scavenge because
		// certain things can only be decided once we have a scavengepoint. however it can do trivial
		// parts of the calculation that are not affected by the scavengepoint. put this doc in the
		// suitable place.
		//
		// SO here we actually only need to calculate the discard point for streams that have metadata
		// so we do it by iterating through the metadatas and calculating the DP for the corresponding
		// original stream.
		//
		// (//qq incidentally, could these maps do with bloom filters for quickly skipping streams that
		// dont need to be scavenged)
		// and we want to know if when we are scavenging the log and index that this is sufficient to
		// discover the discardpoint for every scavengeable stream.
		//
		// what we want to check here, is whether we can always calculate the discard point for the
		// original stream of the metastream and store it, if we only store the original stream hash
		// on the metastreamdata record:
		//   - say neither stram collides with anything
		//       then it is fine, we use the originalstreamhash to look up the lasteventnumber
		//       and, and store the result against the originalstreamhash.
		//   - say the meta stream collides (and we are have a handle to it)
		//       fine. basically same as above
		//   - say the original stream collides, (but the metastreams dont)
		//       means we will have hash handles to metastreamdata objects with original hashes it
		//         where the original hashes are the same.
		//       123 => originalHash: 5
		//       124 => originalHash: 5
		//       we need to (i) notice that 5 is a collision and (ii) find out the stream name that
		//       hashes to it in each case.
		//
		//       remember we have a (very short) list of all the stream names that collide.
		//       we use that to build a map from hashcollision to list of stream names that map to it.
		//       we can then discover that (5) is a collision of "a" and "b", we can then hash
		//       "$$a" and "$$b" until we discover which maps to 123. that will tell us which
		//       stream (a or b) this metadata is for, and give us a streamnamehandle to manipulate it.
		//
		//   - say they both collide. worst case, but actually slightly easier.
		//       then we have:
		//       $$a => originalHash: 5
		//       $$b => originalHash: 5
		//       
		//       we will notice that 5 is a collision, but we will know immediately from the handle
		//       that this is the metadata in stream "$$a" therefore for stream "a"
		//qq ^ remember to implement this resolution
		//
		// maybe the new upshot is we can have something that encapsulates the above logic and given a
		// handle to the metastreamdata and the originalhash, will give us a handle to use for the stream
		// data.
		//
		//
		//
		// for the ones that do not collide, we can do this in the index-only.
		// for the ones that do collide
		//qqq for the ones that do collide can we just not bother to scavenge it for now, but we
		// do want to prove out that it will work later.
		//qq do we need to calculate a discard point for every stream, or can we keep using a discard
		// point that was calculated on a previous scavenge?
		public void Calculate(
			ScavengePoint scavengePoint,
			IMagicForCalculator<TStreamId> scavengeState) {

			// iterate through the metadata streams, for each one use the metadata to modify the
			// discard point of the stream and store it. along the way note down which chunks
			// the records to be discarded.
			foreach (var (metastreamHandle, metastreamData) in scavengeState.MetastreamDatas) {
				var originalStreamHandle = GetOriginalStreamHandle(
					metastreamHandle,
					metastreamData.OriginalStreamHash);

				//qq it would be neat if this interface gave us some hint about the location of
				// the DP so that we could set it in the moment cheaply without having to search
				// although if its a wal that'll be cheap anyway.
				//
				//qq i dont think we can save this lookup by storing it on the metastreamData
				// because when we find, say, the tombstone of the original stream and want to set its
				// DP, the metadata stream does not necessarily exist.
				var originalDiscardPoint = scavengeState.GetOriginalStreamData(originalStreamHandle);

				var adjustedDiscardPoint = CalculateDiscardPointForStream(
					originalStreamHandle,
					originalDiscardPoint,
					metastreamData,
					scavengePoint);

				scavengeState.SetOriginalStreamData(metastreamHandle, adjustedDiscardPoint);
			}
		}

		private StreamHandle<TStreamId> GetOriginalStreamHandle(
			StreamHandle<TStreamId> metastreamHandle,
			ulong originalStreamHash) {

			throw new NotImplementedException();
		}

		// streamHandle: handle to the original stream
		// discardPoint: the discard point previously set by the accumulator
		// metadata: metadata of the original stream (stored in the metadata stream)
		private DiscardPoint CalculateDiscardPointForStream(
			StreamHandle<TStreamId> streamHandle,
			DiscardPoint discardPoint,
			MetastreamData metadata,
			ScavengePoint scavengePoint) {

			//qq our output is
			//   - a map (handle -> DiscardPoint) that lets us look up a discard point for every stream
			//         (this is the same structure used across all the chunks and ptables)
			//          will subsequent scavenges need to recalculate this from
			//          scratch or will some of the discard points still be known to be applicable?)
			//   - and a count of the number of records to discard in each chunk.
			//         (at least approximately)
			//         so that we can decide whether to scavenge a chunk at all or leave it.
			//         //qq will subsequent scavenges count from 0 for each chunk, or somehow pick up
			//         from what was already counted.
			//         //qqqq if we stored the previous and current discard point then can tell
			//         from the index which events are new to scavenge this time - if that helps us
			//         significantly with anything?
			//

			// Events can be discarded because of Tomstones, TruncateBefore, MaxCount, MaxAge.
			// SO:
			// 1. determine an overall discard point from
			//       - a) discard point (this covers Tombstones)
			//       - b) tb
			//       - c) maxcount
			// 2. and a logposition cutoff for
			//       - d) maxage   <- this one does require looking at the location of the events
			// 3. iterate through the eventinfos calling discard for each one that is discarded
			//    by the discard point and logpositioncutoff. this discovers the final discard point.
			//qq ^ this should be done outside of this method.
			// 3. return the final discard point.
			//
			// there are, therefore, three discard points to keep clear, //qq and which all need better names
			//     - the originalDiscardPoint determined by the Accumulator without respect to the
			//       scavengepoint (but includes tombstone, //qq and probably TB)
			//     - the modifiedDiscardPoint which takes into account the maxcount by applying the
			//       scavenge point
			//     - the finalDiscardPoint which takes into account the max age and not discarding the last event
			//
			// the calculator establishes the finalDiscardPoint for all the streams, but there is nothing
			// to do for the metadata streams and tombstoned streams. and they already respect the
			// requirement not to scavenge the last event.

			//qq make sure we never discard the last event (unless that flag is set)
			// i think this means we will always need to get the lastEventNumber

			//qq the stream really should exist but consider what will happen here if it doesn't
			var lastEventNumber = _index.GetLastEventNumber(
				streamHandle,
				scavengePoint.Position);

			var logPositionCutoff = 0L;

			//qq if we are already discarding the maximum, then no need to bother adjusting it
			// or calculating logPositionCutoff. we just discard everything except the tombstone.
			if (discardPoint.IsNotMax()) {
				//qq check these all carefuly

				// Discard more if required by TruncateBefore
				//qq probably build this into the accumulator
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
				var slice = _index.ReadEventInfoForward(streamHandle, fromEventNumber, maxCount);
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
						Discard(eventInfo.LogPosition);
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
		private void Discard(long logPosition) {
			var chunkNumber = (int)(logPosition / TFConsts.ChunkSize);

			if (!_instructionsByChunk.TryGetValue(chunkNumber, out var chunkInstructions)) {
				chunkInstructions = new InMemoryChunkScavengeInstructions<TStreamId>();
				_instructionsByChunk[chunkNumber] = chunkInstructions;
			}

			chunkInstructions.Discard();
		}
	}
}
