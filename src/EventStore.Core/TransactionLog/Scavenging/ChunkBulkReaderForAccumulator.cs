using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Helpers;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq own files for stuff in here
	public class ChunkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
		private readonly TFChunkManager _manager;
		private readonly IMetastreamLookup<TStreamId> _metaStreamLookup;
		private readonly IStreamIdConverter<TStreamId> _streamIdConverter;
		private readonly ICheckpoint _replicationChk;
		private readonly ReadSpecs _readSpecs;

		public ChunkReaderForAccumulator(
			TFChunkManager manager,
			IMetastreamLookup<TStreamId> metastreamLookup,
			IStreamIdConverter<TStreamId> streamIdConverter,
			ICheckpoint replicationChk) {
			_manager = manager;
			_metaStreamLookup = metastreamLookup;
			_streamIdConverter = streamIdConverter;
			_replicationChk = replicationChk;

			// 1 kb should be more than enough to fit the largest metadata record in usual cases
			var reusableRecordBuffer = new ReusableBuffer(1024);
			var reusableBasicPrepare = new ReusableObject<BasicPrepareLogRecord>(() => new BasicPrepareLogRecord());

			void OnRecordDispose() {
				reusableBasicPrepare.Release();
				reusableRecordBuffer.Release();
			}

			_readSpecs = new ReadSpecsBuilder()
				.SkipCommitRecords()
				.SkipSystemRecords()
				.ReadBasicPrepareRecords(
					(streamId, _) => _metaStreamLookup.IsMetaStream(_streamIdConverter.ToStreamId(streamId)),
					delegate { return false; },
					size => {
						var buffer = reusableRecordBuffer.AcquireAsByteArray(size);
						var basicPrepare = reusableBasicPrepare.Acquire(new BasicPrepareInitParams(buffer, OnRecordDispose));
						return basicPrepare;
					}
				);
		}

		public IEnumerable<RecordForAccumulator<TStreamId>> ReadChunk(
			int logicalChunkNumber,
			ReusableObject<RecordForAccumulator<TStreamId>.OriginalStreamRecord> originalStreamRecord,
			ReusableObject<RecordForAccumulator<TStreamId>.MetadataStreamRecord> metadataStreamRecord,
			ReusableObject<RecordForAccumulator<TStreamId>.TombStoneRecord> tombStoneRecord) {
			var chunk = _manager.GetChunk(logicalChunkNumber);
			long globalStartPos = chunk.ChunkHeader.ChunkStartPosition;
			long localPos = 0L;

			var replicationChk = _replicationChk.ReadNonFlushed();
			while (replicationChk == -1 ||
			       globalStartPos + localPos < replicationChk) {
				var result = chunk.TryReadClosestForward(localPos, _readSpecs);

				if (!result.Success)
					break;

				switch (result.LogRecord.RecordType) {
					case LogRecordType.Prepare:
						if (!(result.LogRecord is BasicPrepareLogRecord basicPrepare))
							throw new ArgumentOutOfRangeException(nameof(result.LogRecord), "Expected basic prepare log record.");
						var streamId = _streamIdConverter.ToStreamId(basicPrepare.EventStreamId);
						var initParams = new RecordForAccumulatorInitParams<TStreamId>(basicPrepare, streamId);
						if (basicPrepare.PrepareFlags.HasFlag(PrepareFlags.DeleteTombstone)) {
							yield return tombStoneRecord.Acquire(initParams);
						} else if (_metaStreamLookup.IsMetaStream(streamId)) {
							yield return metadataStreamRecord.Acquire(initParams);
						} else {
							yield return originalStreamRecord.Acquire(initParams);
						}
						break;
					case LogRecordType.Skipped:
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(result.LogRecord.RecordType),
							$"Unexpected log record type: {result.LogRecord.RecordType}");
				}

				localPos = result.NextPosition;
			}
		}
	}

	public class ChunkBulkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
		public IEnumerable<RecordForAccumulator<TStreamId>> ReadChunk(int logicalChunkNumber, ReusableObject<RecordForAccumulator<TStreamId>.OriginalStreamRecord> originalStreamRecord, ReusableObject<RecordForAccumulator<TStreamId>.MetadataStreamRecord> metadataStreamRecord,
			ReusableObject<RecordForAccumulator<TStreamId>.TombStoneRecord> tombStoneRecord) {
			throw new NotImplementedException();
		}
		/*
			//qq once we have index only streams, it could be interesting to have one of those and read that
			// rather than reading the chunks. or it could be a projection
			// or could something like an index-only stream do something like keep the last event
			// per stream for a subset of streams

					// for now just naive iterate through. if we stick with this we want to be able to
			// cancel it mid chunk. (old TraverseChunkBasic does this)
			IEnumerable<TFChunk> GetAllChunks(int startFromChunk) {
				var scavengePosition = db.Config.ChunkSize * (long)startFromChunk;
				var scavengePoint = db.Config.ChaserCheckpoint.Read();
				while (scavengePosition < scavengePoint) {
					var chunk = db.Manager.GetChunkFor(scavengePosition);
					if (!chunk.IsReadOnly) {
						yield break;
					}

					yield return chunk;

					scavengePosition = chunk.ChunkHeader.ChunkEndPosition;
				}
			}

			void TraverseChunkBasic(TFChunk chunk) {
				var result = chunk.TryReadFirst();
				var singletonArray = new IPrepareLogRecord<TStreamId>[1]; //qq stackalloc if we keep this
				while (result.Success) {
					//qq do we need to check if this is committed...
					// can you change metadata in a transaction?
					// can you tombstone in a transaction?
					// this probably doesn't matter if we switch this to continuous.
					//qq letting the accumulator decide what to keep
					//qq should we batch them togther or is presenting one at a time fine
					if (result.LogRecord is IPrepareLogRecord<TStreamId> prepare) {
						singletonArray[0] = prepare;
						accumulator.Confirm(
							replicatedPrepares: singletonArray,
							catchingUp: false, //qq hmm
							backend: null); //qq hmm
					}

					result = chunk.TryReadClosestForward(result.NextPosition);
				}
			}


			//qq refactor with GetStreamMetadataUncached
			var metadata = StreamMetadata.TryFromJsonBytes(prepare);
			var newData = data with {
				MaxAge = metadata.MaxAge,
				MaxCount = metadata.MaxCount,
				TruncateBefore = metadata.TruncateBefore,
			};

		private static bool IsMetadata(IPrepareLogRecord<TStreamId> prepare) {
			// prepare.EventType comparer equals metadataeventtype
			return true; //qqqq
		}

		private static bool IsTombStone(IPrepareLogRecord<TStreamId> prepare) {
			return prepare.ExpectedVersion + 1 == long.MaxValue;
		}


		 */
	}

	//qq implement
	public class ChunkReaderForExecutor : IChunkReaderForExecutor<string> {
		private readonly TFChunk _chunk;

		public ChunkReaderForExecutor(TFChunk chunk) {
			_chunk = chunk;
		}

		public int ChunkStartNumber => _chunk.ChunkHeader.ChunkStartNumber;

		public int ChunkEndNumber => _chunk.ChunkHeader.ChunkEndNumber;

		public bool IsReadOnly => _chunk.IsReadOnly;

		public long ChunkEndPosition => _chunk.ChunkHeader.ChunkEndPosition;

		public IEnumerable<RecordForScavenge<string>> ReadRecords() {
			yield return new RecordForScavenge<string>() {
				StreamId = "thestream",
				TimeStamp = DateTime.UtcNow,
				EventNumber = 123,
				RecordBytes = null,
			};
		}
	}

	public class ChunkWriterForExecutor : IChunkWriterForExecutor<string, TFChunk> {
		private readonly TFChunk _tfChunk;

		public ChunkWriterForExecutor(TFChunkDbConfig dbConfig) {
			_tfChunk = null;
			//qq _tfChunk = TFChunk.CreateNew(...)
		}

		public TFChunk WrittenChunk => _tfChunk;

		public void WriteRecord(RecordForScavenge<string> record) {
			//qq _tfChunk.TryAppend(...)
			throw new NotImplementedException();
		}
	}

	//qq implement
	public class ChunkManagerForScavenge : IChunkManagerForChunkExecutor<string, TFChunk> {
		private readonly TFChunkManager _manager;
		private readonly TFChunkDbConfig _dbConfig;

		public ChunkManagerForScavenge(TFChunkManager manager, TFChunkDbConfig dbConfig) {
			_manager = manager;
			_dbConfig = dbConfig;
		}

		public IChunkWriterForExecutor<string, TFChunk> CreateChunkWriter(
			int chunkStartNumber, //qq pass along
			int chunkEndNumber) {

			return new ChunkWriterForExecutor(_dbConfig);
		}

		public IChunkReaderForExecutor<string> GetChunkReaderFor(long position) {
			var tfChunk = _manager.GetChunkFor(position);
			return new ChunkReaderForExecutor(tfChunk);
		}

		public bool TrySwitchChunk(
			TFChunk chunk,
			bool verifyHash,
			bool removeChunksWithGreaterNumbers,
			out string newFileName) {

			var tfChunk = _manager.SwitchChunk(
				chunk,
				verifyHash,
				removeChunksWithGreaterNumbers);

			if (tfChunk == null) {
				newFileName = default;
				return false;
			} else {
				newFileName = tfChunk.FileName;
				return true;
			}
		}
	}

	//qq
	//public class ChunkBulkReaderForScavenge<TStreamId> : IChunkReaderForChunkExecutor<TStreamId> {
	//	public IEnumerable<RecordForScavenge<TStreamId>> Read(TFChunk chunk) {
	//		throw new NotImplementedException();
	//		//using var reader = chunk.AcquireReader();
	//		//var start = 0;
	//		//var count = ChunkHeader.Size + chunk.ChunkFooter.PhysicalDataSize;

	//		//yield return new RecordForScavenge(); //qq
	//	}
	//}

	//qq
	public class IndexScavenger : IIndexScavenger {
		private readonly ITableIndex _tableIndex;

		public IndexScavenger(ITableIndex tableIndex) {
			_tableIndex = tableIndex;
		}

		public void ScavengeIndex(
			long scavengePoint,
			Func<IndexEntry, bool> shouldKeep,
			IIndexScavengerLog log,
			CancellationToken cancellationToken) {

			//qqq pass scavengePoint into tableindex
			// have it store it in the ptable header, and probaly min it on merge,
			// so it can tell whether any particular ptable can be skipped with respect
			// to a given scavenge point. in this way we save going back to the beginning
			// if we stop the scavenge during the index execution
			_tableIndex.Scavenge(shouldKeep, log, cancellationToken);
		}
	}

	public class ChunkReaderForIndexExecutor : IChunkReaderForIndexExecutor<string> {
		private readonly Func<TFReaderLease> _tfReaderFactory;

		//qq might want to hold the reader for longer than one operation.
		// but this is only called when the hash for htis position is a collision, so rare enough that
		// it probably doesn't matter.
		public ChunkReaderForIndexExecutor(Func<TFReaderLease> tfReaderFactory) {
			_tfReaderFactory = tfReaderFactory;
		}

		public bool TryGetStreamId(long position, out string streamId) {
			using (var reader = _tfReaderFactory()) {
				var result = reader.TryReadAt(position);
				if (!result.Success ||
					!(result.LogRecord is PrepareLogRecord prepare)) {

					streamId = default;
					return false;
				}

				streamId = prepare.EventStreamId;
				return true;
			}
		}
	}
}
