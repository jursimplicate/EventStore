using System;
using System.Threading;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Calculator<TStreamId> : ICalculator<TStreamId> {
		private readonly IIndexReaderForCalculator<TStreamId> _index;
		private readonly int _chunkSize;
		private readonly int _cancellationCheckPeriod;
		private readonly int _checkpointPeriod;

		public Calculator(
			IIndexReaderForCalculator<TStreamId> index,
			int chunkSize,
			int cancellationCheckPeriod,
			int checkpointPeriod) {

			_index = index;
			_chunkSize = chunkSize;
			_cancellationCheckPeriod = cancellationCheckPeriod;
			_checkpointPeriod = checkpointPeriod;
		}

		public void Calculate(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.Calculating<TStreamId> checkpoint,
			IScavengeStateForCalculator<TStreamId> state,
			CancellationToken cancellationToken) {

			if (checkpoint == null) {
				// checkpoint that we are on to calculating now
				state.SetCheckpoint(new ScavengeCheckpoint.Calculating<TStreamId>(default));
			}

			var streamCalc = new StreamCalculator<TStreamId>(_index, scavengePoint);
			var eventCalc = new EventCalculator<TStreamId>(_chunkSize, state, scavengePoint, streamCalc);

			var checkpointCounter = 0;
			var cancellationCheckCounter = 0;

			// iterate through the original (i.e. non-meta) streams that need scavenging (i.e.
			// those that have metadata or tombstones)
			// - for each one use the accumulated data to set/update the discard points of the stream.
			// - along the way add weight to the affected chunks.
			var originalStreamsToScavenge = state.OriginalStreamsToScavenge(
				checkpoint: checkpoint?.DoneStreamHandle ?? default);

			foreach (var (originalStreamHandle, originalStreamData) in originalStreamsToScavenge) {
				//qqqqqqqqqqqqqqqqqqq
				//qq it would be neat if this interface gave us some hint about the location of
				// the DP so that we could set it in a moment cheaply without having to search.
				// although, if its a wal that'll be cheap anyway.
				//qq if the scavengemap supports RMW that might have a bearing too, but for now maybe
				// this is just overcomplicating things.

				//qqqq consider, for subsequent scavenge purposes, how the discard points from the
				// previous, should be accounted for.
				// bear in mind the metadata may have been expanded since the previous disacrdpoints
				// were calculated. should the discard points be allowed to move backwards?
				// suspecting probably not. suspect when you drop a scavengepoint that closes your window
				// to expand the metadata again.... although reads don't know that. hmm.
				// - bear in mind we want it to be deterministic so it might be _necessary_ that we don't
				//   allow the discardpoint to move backwards here
				// - but if we do want to allow it to move backwards we could possibly record more data
				//   here to indicate to the calculator that it has extra work to do
				// - bear in mind that we ought to have discarded data with respect to the old discard
				//   points already so perhaps there isn't harm in moving them backwards
				//
				//qq there is probably scope for a few optimisations here eg, we could store on the
				// originalstreamdata what the lasteventnumber was at the point of calculating the 
				// discard points. then we could spot here that if the last event number hasn't moved
				// and the the metadata hasn't changed, then the DP wont have moved for maxcount.
				// consider the equivalent for the other discard criteria, and see whether the time/space
				// tradeoff is worth it.
				//
				//qq we might also remove from OriginalStreamsToScavenge when the TB or tombstone 
				// is completely spent, which might have a bearing on the above.
				streamCalc.SetStream(originalStreamHandle, originalStreamData);

				CalculateDiscardPointForOriginalStream(
					eventCalc,
					state,
					originalStreamHandle,
					scavengePoint,
					out var adjustedDiscardPoint,
					out var adjustedMaybeDiscardPoint);

				if (adjustedDiscardPoint == originalStreamData.DiscardPoint &&
					adjustedMaybeDiscardPoint == originalStreamData.MaybeDiscardPoint) {
					// nothing to update for this stream
				} else {
					state.SetOriginalStreamDiscardPoints(
						streamHandle: originalStreamHandle,
						discardPoint: adjustedDiscardPoint,
						maybeDiscardPoint: adjustedMaybeDiscardPoint);
				}

				if (++cancellationCheckCounter == _cancellationCheckPeriod) {
					cancellationCheckCounter = 0;
					cancellationToken.ThrowIfCancellationRequested();
				}

				if (++checkpointCounter == _checkpointPeriod) {
					checkpointCounter = 0;
					state.SetCheckpoint(
						new ScavengeCheckpoint.Calculating<TStreamId>(originalStreamHandle));
				}
			}
		}

		// This does two things.
		// 1. Calculates and returns the discard point
		// 2. Adds weight to the affected chunks so that they get scavenged.
		private void CalculateDiscardPointForOriginalStream(
			EventCalculator<TStreamId> eventCalc,
			IScavengeStateForCalculator<TStreamId> state,
			StreamHandle<TStreamId> originalStreamHandle,
			ScavengePoint scavengePoint,
			out DiscardPoint discardPoint,
			out DiscardPoint maybeDiscardPoint) {

			// iterate through the eventInfos in slices.
			// add weight to the chunk the event is in if are discarding or maybe discarding it
			// move the finalDiscardPoint if we are definitely discarding the event.

			//qq this gets set again if we discard something. so, if we don't discard something, what do
			// we want to happen? we probably want to keep it as what we calculated last time - but this
			// will only become apparent when we do subsequent scavenges.
			discardPoint = default;
			//qq this gets set again if we maybe-discard something. same question as above.
			maybeDiscardPoint = default;

			var fromEventNumber = 0L; //qq maybe from the previous scavenge point
			while (true) {
				// read in slices because the stream might be huge.
				// Note: when the handle is a hash the ReadEventInfoForward call is index-only
				//qq limit the read to the scavengepoint too?
				const int maxCount = 100; //qq what would be sensible? probably pretty large

				var slice = _index.ReadEventInfoForward(
					originalStreamHandle,
					fromEventNumber,
					maxCount,
					scavengePoint);

				const float DiscardWeight = 2.0f;
				const float MaybeDiscardWeight = 1.0f;

				//qq naive, we dont need to check every event, we could check the last one**
				foreach (var eventInfo in slice) {
					eventCalc.SetEvent(eventInfo);

					switch (eventCalc.DecideEvent()) {
						case DiscardDecision.Discard:
							AddWeightToChunk(state, eventCalc.LogicalChunkNumber, DiscardWeight);
							discardPoint = DiscardPoint.DiscardIncluding(eventInfo.EventNumber);
							break;

						case DiscardDecision.MaybeDiscard:
							// add weight to the chunk so that this will be inspected more closely
							AddWeightToChunk(state, eventCalc.LogicalChunkNumber, MaybeDiscardWeight);
							maybeDiscardPoint = DiscardPoint.DiscardIncluding(eventInfo.EventNumber);
							break;

						case DiscardDecision.Keep:
							// found the first one to keep. we are done discarding.
							return;

						default:
							throw new Exception("sdfhg"); //qq detail
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
					// so would not having any events in the stream
					// or not having any events from 'fromEventNumber'
					return;
					// in these situatiosn what discard point should we return, or do we need to abort
					//throw new Exception("panic"); //qq dont panic really shouldn't
				}

				fromEventNumber += slice.Length;
			}
		}

		private void AddWeightToChunk(
			IScavengeStateForCalculator<TStreamId> state,
			int logicalChunkNumber,
			float extraWeight) {

			//qq dont want to actually increase the weight every time, just increase it once at the end
			// of the chunk
			//qq also consider when to commit/flush
			state.IncreaseChunkWeight(logicalChunkNumber, extraWeight);
		}
	}
}
