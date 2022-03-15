using System;
using System.Collections.Generic;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class MockIndexReaderForAccumulator : IIndexReaderForAccumulator<string> {
		private readonly ILongHasher<string> _hasher;
		private readonly MockRecord[] _log;

		public MockIndexReaderForAccumulator(
			ILongHasher<string> hasher, MockRecord[] log) {

			_hasher = hasher;
			_log = log;
		}

		public bool HashInUseBefore(ulong hash, long position, out string hashUser) {
			for (int i = 0; i < position; i++) {
				var record = _log[i];
				if (_hasher.Hash(record.StreamName) == hash) {
					hashUser = record.StreamName;
					return true;
				}
			}

			hashUser = default;
			return false;
		}
	}

	public class MockChunkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
		public MockChunkReaderForAccumulator(MockRecord[] log) {

		}
		public IEnumerable<RecordForAccumulator<TStreamId>> Read(int startFromChunk, ScavengePoint scavengePoint) {
			throw new NotImplementedException();
		}
	}


	public class MockIndexForScavenge : IIndexForScavenge<string> {
		public MockIndexForScavenge(MockRecord[] log) {
		}

		public long GetLastEventNumber(StreamHandle<string> stream, long scavengePoint) {
			throw new NotImplementedException();
		}

		public EventInfo[] ReadEventInfoForward(StreamHandle<string> stream, long fromEventNumber, int maxCount) {
			throw new NotImplementedException();
		}
	}

	public class MockChunkReaderForScavenge : IChunkReaderForScavenge<string> {
		public MockChunkReaderForScavenge(MockRecord[] log) {
		}

		public IEnumerable<RecordForScavenge<string>> Read(TFChunk chunk) {
			yield return new() {
				StreamId = "thestream",
				EventNumber = 123,
			};
		}
	}

	public class MockChunkManagerForScavenge : IChunkManagerForScavenge {
		public TFChunk GetChunk(int logicalChunkNum) {
			throw new NotImplementedException();
		}

		public TFChunk SwitchChunk(TFChunk chunk, bool verifyHash, bool removeChunksWithGreaterNumbers) {
			throw new NotImplementedException();
		}
	}
}
