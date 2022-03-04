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

		public IScavengeInstructions<TStreamId> ScavengeInstructions =>
			new InMemoryScavengeInstructions<TStreamId>();

		// iterate through all the scavengeable streams. determine the discard point for each one.
		//
		// we need to do this for the streams with metadata, and the metadata streams.
		// there is a 1-1 correspondance between these.
		// so it would be nice if we could just iterate through one or the other.
		//
		// is storing the two hashes enough.
		//   - say neither collides
		//       then it is fine.
		//   - say the stream collides (and we are have a handle to it)
		//       then the handle will resolve it for us
		//   - say the metastream collides
		//       $$a => 5
		//       $$b => 5
		//       then we notice that 5 collides
		//       so we get all the metadata streams that map to 5
		//       calculate their normal streams, and see which one hashes to us.

		//   - say they both collide. worst case.
		//       we will know we are looking at stream a from its handle.
		//       so we will know that stream $$a is what we want.
		//       a => 5
		//       b => 5
		//       $$a => 5
		//       $$b => 5
		//qq ^ remember to implement this resolution
		//
		//
		// the upshot of this is we can have a CollisionResolver that maps streams to metadata for that
		// stream and _imply_ from it what the metadatastream was and what its discard point is without
		// having to store a separate record for it.

		// for the ones that do not collide, we can do this in the index-only.
		// for the ones that do collide
		//qqq for the ones that do collide can we just not bother to scavenge it for now, but we
		// do want to prove out that it will work later.
		//qq do we need to calculate a discard point for every stream, or can we keep using a discard
		// point that was calculated on a previous scavenge?
		public void Calculate(ScavengePoint scavengePoint, IMagicForCalculator<TStreamId> scavengeState) {
			//qq the order that we calculate the discard points in isn't important is it?
			// iterate through the streams with metadata, calculating discard points
			// we will need to scavenge the metadata streams too, but calculating the discard points
			// for them is trivial
			foreach (var (streamHandle, streamData) in scavengeState.StreamsWithMetadata) {
				var dp = CalculateDiscardPointForStream(
					streamHandle,
					streamData,
					scavengePoint);

				scavengeState.SetDiscardPoint(streamHandle, dp);
			}
		}

		private DiscardPoint CalculateDiscardPointForStream(
			StreamHandle<TStreamId> streamHandle,
			StreamData streamData,
			ScavengePoint scavengePoint) {

			//qq our output is
			//   - a map that lets us look up a decision point for every stream (this is the same acros
			//         all the chunks. //qq will subsequent scavenges need to recalculate this from
			//         scratch or will some of the discard points still be known to be applicable?
			//   - and a count of the number of records to discard in each chunk.
			//         so that we can decide whether to scavenge a chunk at all.
			//         //qq will subsequent scavenges count from 0 for each chunk, or somehow pick up
			//         from what was already counted.
			//         //qqqq if we stored the previous and current discard point then can tell
			//         from the index which events are new to scavenge this time - if that helps us
			//         significantly with anything?
			//

			// SO: read the stream from the index forward. in slices (since 1bn events is too much for
			// one go).
			//  - iterate through them calling discard for each one that we are discarding
			//    so that each chunk knows how many records are getting discarded from it.
			//

			//qq prolly grab the latesteventnumber out here

			const int maxCount = 100; //qq what would be sensible? probably pretty large
			var fromEventNumber = 0L;
			while (true) {
				//qq limit the read to the scavengepoint too?
				var slice = _index.ReadStreamForward(streamHandle, fromEventNumber, maxCount);
				//qq naive, we dont need to check every event, we could check the last one
				// and if that is to be discarded then we can discard everything in this slice.
				foreach (var evt in slice) {
					//qq important, want to keep the last event in the stream,
					// currently this isn't done in shouldkeep. add it there, or handle it out here
					// (we are reading through the stream so we know what the last one is)
					//qqqqq gotta respect the scavenge point also. do that out here or
					//  let shouldkeep do it. we want to keep everything on or after the scavenge point
					// if shouldkeep doesn't so it, rename it to indicate the scope of what it does
					// calculate.
					if (ShouldKeep(
							scavengePoint,
							streamHandle,
							streamData,
							evt.EventNumber,
							evt.LogPosition)) {
						// found the first one to keep. we are done discarding.
						return new DiscardPoint(123); //qqqqqq
					}

					Discard(evt.LogPosition);
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

			//qq need to set the discard point.
			//return new DiscardPoint(123); //qqqqqq
		}

		private bool ShouldKeep(
			ScavengePoint scavengePoint,
			StreamHandle<TStreamId> stream,
			StreamData streamData,
			long eventNumber,
			long logPosition) {
			//qqqq important, we want to discard metadata records when they aren't the latest ones
			// for their stream. but we dont write explicit metadata records for metadata streams, so
			// we wont have accumulated it.
			// but we could say when we are calculating a stream, we could also calculate and discard
			// things from its metadta stream

			//qq check/test these carefully obviously
			if (DiscardBecauseHardDeleted(streamData, eventNumber: eventNumber))
				return false;

			if (IsExpiredByTruncateBefore(streamData, eventNumber: eventNumber))
				return false;

			if (IsExpiredByMaxAge(scavengePoint, streamData, logPosition: logPosition))
				return false;

			if (IsExpiredByMaxCount(scavengePoint, stream, streamData, eventNumber: eventNumber))
				return false;

			return true;
		}

		static bool DiscardBecauseHardDeleted(
			StreamData streamData,
			long eventNumber) {

			// keep tombstone
			return streamData.IsHardDeleted && eventNumber < long.MaxValue;
		}

		static bool IsExpiredByTruncateBefore(
			StreamData streamData,
			long eventNumber) {

			if (streamData.TruncateBefore == null)
				return false;

			var expired = eventNumber < streamData.TruncateBefore.Value;
			return expired;
		}

		//qq nb: index-only shortcut for maxage works for transactions too because it is the
		// prepare timestamp that we use not the commit timestamp.
		static bool IsExpiredByMaxAge(
			ScavengePoint scavengePoint,
			StreamData streamData,
			long logPosition) {

			if (streamData.MaxAge == null)
				return false;

			//qq a couple of these methods calculate the chunk number, consider passing it in directly
			var chunkNumber = (int)(logPosition / TFConsts.ChunkSize);

			// We can discard the event when it is as old or older than the cutoff
			var cutoff = scavengePoint.EffectiveNow - streamData.MaxAge.Value;

			// but we weaken the condition to say we only discard when the whole chunk is older than
			// the cutoff (which implies that the event certainly is)
			// the whole chunk is older than the cutoff only when the next chunk started before the cutoff
			var nextChunkCreatedAt = GetChunkCreatedAt(chunkNumber + 1);
			//qq ^ consider if there might not be a next chunk
			// say we closed the last chunk (this one) exactly on a boundary and haven't created the next one yet.

			// however, consider clock skew. we want to avoid the case where we accidentally discard a
			// record that we should have kept, because the chunk stamp said discard but the real record
			// stamp would have said keep.
			// for this to happen the records stamp would have to be newer than the chunk stamp.
			// add a maxSkew to the nextChunkCreatedAt to make it discard less.
			var nextChunkCreatedAtIncludingSkew = nextChunkCreatedAt + TimeSpan.FromMinutes(1); //qq make configurable
			var discard = nextChunkCreatedAtIncludingSkew <= cutoff;
			return discard;

			DateTime GetChunkCreatedAt(int chunkNumber) {
				throw new NotImplementedException(); //qq
			}
		}

		bool IsExpiredByMaxCount(
			ScavengePoint scavengePoint,
			StreamHandle<TStreamId> stream,
			StreamData streamData,
			long eventNumber) {

			if (streamData.MaxCount == null)
				return false;

			// say eventNumber is 3
			// and streamData.MaxCount = 2
			// then this event will expire when we write event number 5
			// because the live events at that point will be events 4 and 5.

			var expires = eventNumber + streamData.MaxCount;

			//qq the stream really should exist but what will happen here if it doesn't
			//qqqq we're going to hit this a lot, some kind of cache would be in order
			//qqqqqqqqqqqqqq BUT we are iterating through the stream so maybe we can
			// do something cunning
			var lastEventNumber = _index.GetLastEventNumber(
				stream,
				scavengePoint.Position);

			return expires <= lastEventNumber;
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
