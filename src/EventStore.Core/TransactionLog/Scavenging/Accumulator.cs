using System;
using System.Threading;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Accumulator<TStreamId> : IAccumulator<TStreamId> {
		private readonly IMetastreamLookup<TStreamId> _metastreamLookup;
		private readonly IChunkReaderForAccumulator<TStreamId> _chunkReader;
		private readonly int _cancellationCheckPeriod;

		public Accumulator(
			IMetastreamLookup<TStreamId> metastreamLookup,
			IChunkReaderForAccumulator<TStreamId> chunkReader,
			int cancellationCheckPeriod) {

			_metastreamLookup = metastreamLookup;
			_chunkReader = chunkReader;
			_cancellationCheckPeriod = cancellationCheckPeriod;
		}

		public void Accumulate(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.Accumulating checkpoint,
			IScavengeStateForAccumulator<TStreamId> state,
			CancellationToken cancellationToken) {

			//qq todo finish in right place, which is at latest the last open chunk when we get there
			// or the scavenge point.
			//qq stop before the scavenge point, or perhaps more likely at the end of the chunk before
			// the chunk that has the scavenge point in because at the time the scavenge point is written
			// that is as far as we can scavenge up to.

			if (checkpoint == null) {
				// checkpoint that we are on to accumulating now
				state.SetCheckpoint(new ScavengeCheckpoint.Accumulating(null));
			}

			var logicalChunkNumber = checkpoint?.DoneLogicalChunkNumber + 1 ?? 0;

			try {
				while (AccumulateChunkAndRecordRange(
						scavengePoint,
						state,
						logicalChunkNumber,
						cancellationToken)) {

					cancellationToken.ThrowIfCancellationRequested();
					logicalChunkNumber++;
				}
			}
			catch (ArgumentOutOfRangeException) {
				//qq once the tests have scavengepoints we can remove this try/catch
				// because the scavengepoints will stop the enumeration instead of
				// running off of the end of the log like this
			}
		}

		// returns true to continue
		private bool AccumulateChunkAndRecordRange(
			ScavengePoint scavengePoint,
			IScavengeStateForAccumulator<TStreamId> state,
			int logicalChunkNumber,
			CancellationToken cancellationToken) {

			var ret = AccumulateChunk(
				scavengePoint,
				state,
				logicalChunkNumber,
				cancellationToken,
				out var chunkMinTimeStamp,
				out var chunkMaxTimeStamp);

			if (chunkMinTimeStamp <= chunkMaxTimeStamp) {
				state.SetChunkTimeStampRange(
					logicalChunkNumber: logicalChunkNumber,
					new ChunkTimeStampRange(
						min: chunkMinTimeStamp,
						max: chunkMaxTimeStamp));
			} else {
				// empty range, no need to store it.
			}

			state.SetCheckpoint(new ScavengeCheckpoint.Accumulating(
				doneLogicalChunkNumber: logicalChunkNumber));

			return ret;
		}

		// returns true to continue
		// do not assume that record TimeStamps are non descending, clocks can change.
		private bool AccumulateChunk(
			ScavengePoint scavengePoint,
			IScavengeStateForAccumulator<TStreamId> state,
			int logicalChunkNumber,
			CancellationToken cancellationToken,
			out DateTime chunkMinTimeStamp,
			out DateTime chunkMaxTimeStamp) {

			// start with empty range and expand it as we discover records.
			chunkMinTimeStamp = DateTime.MaxValue;
			chunkMaxTimeStamp = DateTime.MinValue;

			var stopBefore = scavengePoint.Position;
			var cancellationCheckCounter = 0;
			foreach (var record in _chunkReader.ReadChunk(logicalChunkNumber)) {
				if (record.LogPosition >= stopBefore) {
					return false;
				}

				if (record.TimeStamp < chunkMinTimeStamp)
					chunkMinTimeStamp = record.TimeStamp;

				if (record.TimeStamp > chunkMaxTimeStamp)
					chunkMaxTimeStamp = record.TimeStamp;

				switch (record) {
					//qq there may be other cases... the 'empty write', the system record (epoch)
					// which we might still want to get the timestamp of.
					// oh and commit records which we probably want to handle the same as prepares
					case RecordForAccumulator<TStreamId>.OriginalStreamRecord x:
						ProcessOriginalStreamRecord(x, state);
						break;
					case RecordForAccumulator<TStreamId>.MetadataStreamRecord x:
						ProcessMetastreamRecord(x, state);
						break;
					case RecordForAccumulator<TStreamId>.TombStoneRecord x:
						ProcessTombstone(x, state);
						break;
					default:
						throw new NotImplementedException(); //qq
				}

				if (++cancellationCheckCounter == _cancellationCheckPeriod) {
					cancellationCheckCounter = 0;
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			return true;
		}

		// For every* record in an original stream we need to see if its stream collides.
		// its not so bad, because we have a cache
		// * maybe not every.. beyond a certain point we only need to check records with eventnumber 0
		//    (and perhaps -1 to cover transactions)
		//    but doing so has some complications
		//      - have to identify the point at which we can switch to just checking 0s
		//qq    - is it possible that a new stream starts at non-zero without already having been checked
		//        before? seems unlikely.. 
		private static void ProcessOriginalStreamRecord(
			RecordForAccumulator<TStreamId>.OriginalStreamRecord record,
			IScavengeStateForAccumulator<TStreamId> state) {

			state.DetectCollisions(record.StreamId);
		}

		// For every record in a metadata stream
		//   - check if the metastream or originalstream collide with anything
		//   - store the metadata against the original stream so the calculator can calculate the
		//         discard point.
		//   - update the discard point of the metadatastream
		// the actual type of the record isn't relevant. if it is in a metadata stream it affects
		// the metadata. if its data parses to streammetadata then thats the metadata. if it doesn't
		// parse, then it clears the metadata.
		private void ProcessMetastreamRecord(
			RecordForAccumulator<TStreamId>.MetadataStreamRecord record,
			IScavengeStateForAccumulator<TStreamId> state) {

			var originalStreamId = _metastreamLookup.OriginalStreamOf(record.StreamId);
			state.DetectCollisions(originalStreamId);
			state.DetectCollisions(record.StreamId);

			if (_metastreamLookup.IsMetaStream(originalStreamId)) {
				// record in a metadata stream of a metadata stream: $$$$xyz
				// this does not set metadata for $$xyz (which is fixed at maxcount1)
				// but it does, itself, have a fixed metadta of maxcount1, so move the discard point.
			} else {
				// record is in a standard metadata stream: $$xyz
				// Update the Metadata for stream xyz
				state.SetOriginalStreamMetadata(originalStreamId, record.Metadata);
			}

			if (record.EventNumber < 0)
				throw new InvalidOperationException(
					$"Found metadata in transaction in stream {record.StreamId}");

			// Update the discard point
			var discardPoint = DiscardPoint.DiscardBefore(record.EventNumber);
			state.SetMetastreamDiscardPoint(record.StreamId, discardPoint);
		}

		// For every tombstone
		//   - check if the stream collides
		//   - set the discard point for the stream that the tombstone was found in to discard
		//     everything before the tombstone
		//   - set the istombstoned flag to true
		private void ProcessTombstone(
			RecordForAccumulator<TStreamId>.TombStoneRecord record,
			IScavengeStateForAccumulator<TStreamId> state) {

			state.DetectCollisions(record.StreamId);

			if (_metastreamLookup.IsMetaStream(record.StreamId)) {
				// isn't possible to write a tombstone to a metadatastream, but spot it in case
				// it ever was possible.
				throw new InvalidOperationException(
					$"Found Tombstone in metadata stream {record.StreamId}");
			}

			state.SetOriginalStreamTombstone(record.StreamId);
		}
	}
}
