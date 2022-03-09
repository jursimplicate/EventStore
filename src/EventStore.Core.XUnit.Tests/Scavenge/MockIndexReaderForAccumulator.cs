using EventStore.Core.Index.Hashes;
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
}
