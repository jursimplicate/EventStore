using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq own files for stuff in here
	public class ChunkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
		public IEnumerable<RecordForAccumulator<TStreamId>> Read(int startFromChunk, ScavengePoint scavengePoint) {
			throw new NotImplementedException();
		}
	}

	public class ChunkBulkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
		public IEnumerable<RecordForAccumulator<TStreamId>> Read(int startFromChunk, ScavengePoint scavengePoint) {
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
	public class ChunkReaderForScavenge : IChunkReaderForScavenge<string> {
		public IEnumerable<RecordForScavenge<string>> Read(TFChunk chunk) {
			yield return new() {
				StreamId = "thestream",
				EventNumber = 123,
			};
		}
	}

	//qq implement
	public class ChunkManagerForScavenge : IChunkManagerForScavenge {
		public TFChunk GetChunk(int logicalChunkNum) {
			throw new NotImplementedException();
		}

		public TFChunk SwitchChunk(TFChunk chunk, bool verifyHash, bool removeChunksWithGreaterNumbers) {
			throw new NotImplementedException();
		}
	}

	public class ChunkBulkReaderForScavenge<TStreamId> : IChunkReaderForScavenge<TStreamId> {
		public IEnumerable<RecordForScavenge<TStreamId>> Read(TFChunk chunk) {
			throw new NotImplementedException();
			//using var reader = chunk.AcquireReader();
			//var start = 0;
			//var count = ChunkHeader.Size + chunk.ChunkFooter.PhysicalDataSize;

			//yield return new RecordForScavenge(); //qq
		}
	}
}
