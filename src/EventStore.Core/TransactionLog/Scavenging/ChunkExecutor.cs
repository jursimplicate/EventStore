using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq add logging to this and the other stages
	public class ChunkExecutor<TStreamId, TChunk> : IChunkExecutor<TStreamId> {

		private readonly IMetastreamLookup<TStreamId> _metastreamLookup;
		private readonly IChunkManagerForChunkExecutor<TStreamId, TChunk> _chunkManager;
		private readonly long _chunkSize;
		private readonly int _cancellationCheckPeriod;

		public ChunkExecutor(
			IMetastreamLookup<TStreamId> metastreamLookup,
			IChunkManagerForChunkExecutor<TStreamId, TChunk> chunkManager,
			long chunkSize,
			int cancellationCheckPeriod) {

			_metastreamLookup = metastreamLookup;
			_chunkManager = chunkManager;
			_chunkSize = chunkSize;
			_cancellationCheckPeriod = cancellationCheckPeriod;
		}

		public void Execute(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.ExecutingChunks checkpoint,
			IScavengeStateForChunkExecutor<TStreamId> state,
			CancellationToken cancellationToken) {

			if (checkpoint == null) {
				// checkpoint that we are on to chunk execution now
				state.SetCheckpoint(new ScavengeCheckpoint.ExecutingChunks(null));
			}

			//qq would we want to run in parallel? (be careful with scavenge state interactions
			// in that case, especially writes and storing checkpoints)
			//qq order by the weight? maybe just iterate backwards.

			//qq there is no point scavenging beyond the scavenge point
			//qqqq is +1 ok range wise? same for accumulator
			var startFromChunk = checkpoint?.DoneLogicalChunkNumber + 1 ?? 0; //qq necessarily zero?

			foreach (var physicalChunk in GetAllPhysicalChunks(startFromChunk, scavengePoint.Position)) {
				var physicalWeight = WeighPhysicalChunk(state, physicalChunk);

				//qq configurable threshold? in scavenge point?
				var threshold = 0.0f;
				if (physicalWeight < threshold) {
					// they'll still (typically) be removed from the index
					return;
				}

				ExecutePhysicalChunk(scavengePoint, state, physicalChunk, cancellationToken);

				foreach (var logicalChunkNumber in physicalChunk.LogicalChunkNumbers) {
					//qq when to commit/flush, immediately probably
					//qqq oh this clearly wants to take a min/max and delete the range
					state.ResetChunkWeight(logicalChunkNumber);
				}

				state.SetCheckpoint(
					new ScavengeCheckpoint.ExecutingChunks(physicalChunk.ChunkEndNumber));

				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		private float WeighPhysicalChunk(
			IScavengeStateForChunkExecutor<TStreamId> state,
			IChunkReaderForExecutor<TStreamId> physicalChunk) {

			// add together the weights of each of the logical chunks in this physical chunk.
			var totalWeight = 0.0f;
			foreach (var logicalChunkNumber in physicalChunk.LogicalChunkNumbers) {
				//qqq this also wants to do a sum over the range.
				if (state.TryGetChunkWeight(logicalChunkNumber, out var weight)) {
					totalWeight += weight;
				}
			}

			return totalWeight;
		}

		private IEnumerable<IChunkReaderForExecutor<TStreamId>> GetAllPhysicalChunks(
			int startFromChunk,
			long upTo) {

			var scavengePos = _chunkSize * startFromChunk;
			while (scavengePos < upTo) {
				var physicalChunk = _chunkManager.GetChunkReaderFor(scavengePos);

				if (!physicalChunk.IsReadOnly)
					yield break;

				yield return physicalChunk;

				scavengePos = physicalChunk.ChunkEndPosition;
			}

		}

		private void ExecutePhysicalChunk(
			ScavengePoint scavengePoint,
			IScavengeStateForChunkExecutor<TStreamId> state,
			IChunkReaderForExecutor<TStreamId> chunk,
			CancellationToken cancellationToken) {

			//qq the other reason we might want to not scanvenge this chunk is if the posmap would make
			// it bigger
			// than the original... limited concern because of the threshold above BUT we could address
			// by using a padding/scavengedevent system event to prevent having to write a posmap
			// this is the kind of decision we can make in here, local to the chunk.
			// knowing the numrecordstodiscard could be useful here, if we are just discarding a small
			// number then we'd probably pad them with 'gone' events instead of adding a posmap.

			//qq in ExecuteChunk could also be a reasonable place to do a best effort at removing commit
			// records if all the prepares for the commit are in this chunk (typically the case) and they
			// are all scavenged, then we can remove the commit as well i think. this is probably what
			// the old scavenge does. check

			//qq old scavenge says 'never delete the very first prepare in a transaction'
			// hopefully we can account for that here? although maybe it means our count of
			// records to scavenge that was calculated index only might end up being approximate.

			// 1. open the chunk, probably with the bulk reader
			var newChunk = _chunkManager.CreateChunkWriter(
				chunk.ChunkStartNumber,
				chunk.ChunkEndNumber);

			var cancellationCheckCounter = 0;
			foreach (var record in chunk.ReadRecords()) {
				var discard = ShouldDiscard(
					state,
					scavengePoint,
					record);

				if (discard) {
					//qq discard record
				} else {
					//qq keep record
					newChunk.WriteRecord(record); //qq or similar
					//qq do we need to upgrade it?
					//qq will using the bulk reader be awkward considering the record format
					// size changes that have occurred over the years
					// if so consider using the regular reader.
					// what does the old scavenge use
					// consider transactions
				}

				if (++cancellationCheckCounter == _cancellationCheckPeriod) {
					cancellationCheckCounter = 0;
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			// 2. read through it, keeping and discarding as necessary. probably no additional lookups at
			// this point
			// 3. write the posmap
			// 4. finalise the chunk
			// 5. swap it in to the chunkmanager
			if (_chunkManager.TrySwitchChunk(
				newChunk.WrittenChunk,
				verifyHash: default, //qq
				removeChunksWithGreaterNumbers: default, //qq
				out var newFileName)) {
				//qq what is the new file name of an inmemory chunk :/
				//qq log
			} else {
				//qq log
			}
		}

		private bool ShouldDiscard(
			IScavengeStateForChunkExecutor<TStreamId> state,
			ScavengePoint scavengePoint,
			RecordForScavenge<TStreamId> record) {

			if (record.EventNumber < 0) {
				//qq we can discard from transactions sometimes, copy the logic from old scavenge.
				return false;
			}

			//qq consider how/where to cache the this stuff per stream for quick lookups
			var details = GetStreamExecutionDetails(
				state,
				record.StreamId);

			// if definitePoint says discard then discard.
			if (details.DiscardPoint.ShouldDiscard(record.EventNumber)) {
				return true;
			}

			// if maybeDiscardPoint says discard then maybe we can discard - depends on maxage
			if (!details.MaybeDiscardPoint.ShouldDiscard(record.EventNumber)) {
				// both discard points said do not discard, so dont.
				return false;
			}

			if (!details.MaxAge.HasValue) {
				return false;
			}

			return record.TimeStamp < scavengePoint.EffectiveNow - details.MaxAge;
		}

		private StreamExecutionDetails GetStreamExecutionDetails(
			IScavengeStateForChunkExecutor<TStreamId> state,
			TStreamId streamId) {

			if (_metastreamLookup.IsMetaStream(streamId)) {
				if (!state.TryGetMetastreamDiscardPoint(streamId, out var discardPoint)) {
					discardPoint = DiscardPoint.KeepAll;
				}

				return new StreamExecutionDetails(
					discardPoint: discardPoint,
					maybeDiscardPoint: DiscardPoint.KeepAll,
					maxAge: null);
			} else {
				// original stream
				if (state.TryGetStreamExecutionDetails(streamId, out var details)) {
					return details;
				} else {
					return new StreamExecutionDetails(
						discardPoint: DiscardPoint.KeepAll,
						maybeDiscardPoint: DiscardPoint.KeepAll,
						maxAge: null);
				}
			}
		}
	}
}
