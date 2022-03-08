using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Accumulator<TStreamId> : IAccumulator<TStreamId> {
		private readonly IMetastreamLookup<TStreamId> _metastreamLookup;
		private readonly IChunkReaderForAccumulation<TStreamId> _chunkReader;
		private readonly ILongHasher<TStreamId> _hasher = null; //qqqqqqqqq set in ctor

		public Accumulator(
			ILongHasher<TStreamId> hasher,
			IMetastreamLookup<TStreamId> metastreamLookup,
			IChunkReaderForAccumulation<TStreamId> chunkReader) {

			_metastreamLookup = metastreamLookup;
			_chunkReader = chunkReader;
		}

		public IMagicForCalculator<TStreamId> ScavengeState =>
			throw new NotImplementedException();

		//qq condider what requirements this has of the chunkreader in terms of transactions
		//qq are we expecting to read only committed records?
		//qq are we expecting to read the records in commitPosition order?
		//     (if so bulkreader might not be ideal)
		//       or prepareposition order
		//qq in fact we should probably do a end to end ponder of transactions (commit position vs logposition)
		//    - also out-of-order and duplicate events
		//    - partial scavenge where events have been removed from the log but not the index yet
		//    - a completed previous scavenge
		public void Accumulate(
			ScavengePoint scavengePoint,
			IMagicForAccumulator<TStreamId> magic) {

			var records = _chunkReader.Read(startFromChunk: 0, scavengePoint);
			foreach (var record in records) {
				switch (record) {
					case RecordForAccumulator<TStreamId>.EventRecord x:
						Accumulate(x, magic);
						break;
					case RecordForAccumulator<TStreamId>.MetadataRecord x:
						Accumulate(x, magic);
						break;
					case RecordForAccumulator<TStreamId>.TombStoneRecord x:
						Accumulate(x, magic);
						break;
					default:
						throw new NotImplementedException(); //qq
				}
			}
		}

		// For every (//qq ?) event we need to see if its stream collides.
		// its not so bad, because we have a cache
		//qq - how does the cache work, we could just cache the fact that we have already
		//   notified for this stream and not bother notifying again (we should still checkpoint that we got this far though)
		//   OR we can cache the user of a hash against that hash. which has the advantage that if we
		//      do come across a hash collision it might already be in the cache. but this is so rare
		//      as to not be a concern. pick whichever turns out to be more obviously correct
		private static void Accumulate(
			RecordForAccumulator<TStreamId>.EventRecord record,
			IMagicForAccumulator<TStreamId> magic) {
			//qq hmm for transactions does this need to be the prepare log position,
			// the commit log position, or, in fact, both? can metadata be written as part of a transaction
			// if so does that mean we need to do anything special when handling a metadata record
			magic.NotifyForCollisions(record.StreamId, record.LogPosition);
		}

		// For every metadata record
		//   - check if the stream collides
		//   - cache the metadata against the metadatastream handle.
		//         this causes scavenging of the original stream and the metadata stream.
		//qq definitely add a test that metadata records get scavenged though
		private void Accumulate(
			RecordForAccumulator<TStreamId>.MetadataRecord record,
			IMagicForAccumulator<TStreamId> magic) {

			magic.NotifyForCollisions(record.StreamId, record.LogPosition);

			var streamData = magic.GetMetastreamData(record.StreamId);

			//qqqq set the new stream data, leave the harddeleted flag alone.
			// consider if streamdata really wants to be immutable. also c# records not supported in v5
			var originalStream = _metastreamLookup.OriginalStreamOf(record.StreamId);
			var newStreamData = streamData with {
				OriginalStreamHash = _hasher.Hash(originalStream),
				MaxAge = null, //qq actually set these correctly
				MaxCount = 345,
				TruncateBefore = 567,
				//qq probably only want to increase the discard point here, in order to respect tombstone
				// although... if there was a tombstone then this record shouldn't exist, and if it does
				// we probably want to ignore it
				DiscardPoint = null, //qq record.EventNumber (or evtnuber - 1)
				//IsHardDeleted = ,
				//IsMetadataStreamHardDeleted = 
			};

			magic.SetMetastreamData(record.StreamId, newStreamData);
		}

		// For every tombstone
		//   - check if the stream collids
		//   - set the discard point for the stream that the tombstone was found in to discard
		//     everything before the tombstone
		private void Accumulate(
			RecordForAccumulator<TStreamId>.TombStoneRecord record,
			IMagicForAccumulator<TStreamId> magic) {

			magic.NotifyForCollisions(record.StreamId, record.LogPosition);

			// it is possible, though maybe very unusual, to find a tombstone in a metadata stream
			if (_metastreamLookup.IsMetaStream(record.StreamId)) {
				var streamData = magic.GetMetastreamData(record.StreamId);
				streamData = streamData with {
					DiscardPoint = DiscardPoint.Tombstone,
				};
				magic.SetMetastreamData(record.StreamId, streamData);
			} else {
				// get the streamData for the stream, tell it the stream is deleted
				magic.SetOriginalStreamData(record.StreamId, DiscardPoint.Tombstone);
			}
		}

		private void AccumulateTimeStamps(int ChunkNumber, DateTime createdAt) {
			//qq call this. consider name
			// actually make this add to magicmap, separate datastructure for the timestamps
			// idea is to decide whether a record can be discarded due to maxage just
			// by looking at its logposition (i.e. index-only)
			// needs configurable leeway for clockskew
		}
	}
}
