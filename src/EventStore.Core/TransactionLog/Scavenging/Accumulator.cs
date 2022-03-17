using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Accumulator<TStreamId> : IAccumulator<TStreamId> {
		private readonly ILongHasher<TStreamId> _hasher;
		private readonly IMetastreamLookup<TStreamId> _metastreamLookup;
		private readonly IChunkReaderForAccumulator<TStreamId> _chunkReader;

		public Accumulator(
			ILongHasher<TStreamId> hasher,
			IMetastreamLookup<TStreamId> metastreamLookup,
			IChunkReaderForAccumulator<TStreamId> chunkReader) {

			_hasher = hasher;
			_metastreamLookup = metastreamLookup;
			_chunkReader = chunkReader;
		}

		public void Accumulate(
			ScavengePoint scavengePoint,
			IScavengeStateForAccumulator<TStreamId> state) {

			var records = _chunkReader.Read(startFromChunk: 0, scavengePoint);
			foreach (var record in records) {
				switch (record) {
					//qq there may be other cases... the 'empty write', the system record (epoch)
					// which we might still want to get the timestamp of.
					// oh and commit records which we probably want to handle the same as prepares
					case RecordForAccumulator<TStreamId>.EventRecord x:
						ProcessEvent(x, state);
						break;
					case RecordForAccumulator<TStreamId>.MetadataRecord x:
						ProcessMetadata(x, state);
						break;
					case RecordForAccumulator<TStreamId>.TombStoneRecord x:
						ProcessTombstone(x, state);
						break;
					default:
						throw new NotImplementedException(); //qq
				}
			}
		}

		// For every* event we need to see if its stream collides.
		// its not so bad, because we have a cache
		// * maybe not every.. beyond a certain point we only need to check records with eventnumber 0
		//    but doing so has some complications
		//      - events in transactions dont have an event number... we could just check all of these
		//      - have to identify the point at which we can switch to just checking 0s
		//      - is it possible that a new stream starts at non-zero without already having been checked
		//        before? seems unlikely.. 
		//qq - how does the cache work, we could just cache the fact that we have already
		//   notified for this stream and not bother notifying again (we should still checkpoint that we
		//     got this far though)
		//   OR we can cache the user of a hash against that hash. which has the advantage that if we
		//      do come across a hash collision it might already be in the cache. but this is so rare
		//      as to not be a concern. pick whichever turns out to be more obviously correct
		private static void ProcessEvent(
			RecordForAccumulator<TStreamId>.EventRecord record,
			IScavengeStateForAccumulator<TStreamId> state) {
			//qq hmm for transactions does this need to be the prepare log position,
			// the commit log position, or, in fact, both? it would need to be the prepare position.
			//qq can metadata be written as part of a transaction (decided will detect and abort as an
			// unsupported case)
			state.NotifyForCollisions(record.StreamId, record.LogPosition);
		}

		// For every metadata record
		//   - check if the stream collides
		//   - cache the metadata against the metadatastream handle.
		//         this causes scavenging of the original stream and the metadata stream.
		//qq definitely add a test that metadata records get scavenged though
		private void ProcessMetadata(
			RecordForAccumulator<TStreamId>.MetadataRecord record,
			IScavengeStateForAccumulator<TStreamId> state) {

			state.NotifyForCollisions(record.StreamId, record.LogPosition);

			if (!state.TryGetMetastreamData(record.StreamId, out var metaStreamData))
				metaStreamData = MetastreamData.Empty;

			//qqqq set the new stream data, leave the harddeleted flag alone.
			// consider if streamdata really wants to be immutable. also c# records not supported in v5
			var originalStream = _metastreamLookup.OriginalStreamOf(record.StreamId);
			var newStreamData = metaStreamData with {
				OriginalStreamHash = _hasher.Hash(originalStream),
				MaxAge = record.Metadata.MaxAge,
				MaxCount = record.Metadata.MaxCount,
				TruncateBefore = record.Metadata.TruncateBefore,
				//qq probably only want to increase the discard point here, in order to respect tombstone
				// although... if there was a tombstone then this record shouldn't exist, and if it does
				// we probably want to ignore it
				//qq event number wont be set if this metadata is in a transaction,
				// unless we use teh allreader.
				DiscardPoint = DiscardPoint.DiscardBefore(record.EventNumber),
			};

			state.SetMetastreamData(record.StreamId, newStreamData);
		}

		// For every tombstone
		//   - check if the stream collides
		//   - set the discard point for the stream that the tombstone was found in to discard
		//     everything before the tombstone
		private void ProcessTombstone(
			RecordForAccumulator<TStreamId>.TombStoneRecord record,
			IScavengeStateForAccumulator<TStreamId> state) {

			state.NotifyForCollisions(record.StreamId, record.LogPosition);

			// it is possible, though maybe very unusual, to find a tombstone in a metadata stream
			if (_metastreamLookup.IsMetaStream(record.StreamId)) {
				if (!state.TryGetMetastreamData(record.StreamId, out var metaStreamData))
					metaStreamData = MetastreamData.Empty;

				metaStreamData = metaStreamData with {
					DiscardPoint = DiscardPoint.Tombstone,
				};
				state.SetMetastreamData(record.StreamId, metaStreamData);
			} else {
				// get the streamData for the stream, tell it the stream is deleted
				state.SetOriginalStreamData(record.StreamId, DiscardPoint.Tombstone);
			}
		}

		private void AccumulateTimeStamps(int ChunkNumber, DateTime createdAt) {
			//qq call this. consider name
			// actually make this add to state, separate datastructure for the timestamps
			// idea is to decide whether a record can be discarded due to maxage just
			// by looking at its logposition (i.e. index-only)
			// needs configurable leeway for clockskew
		}
	}
}
