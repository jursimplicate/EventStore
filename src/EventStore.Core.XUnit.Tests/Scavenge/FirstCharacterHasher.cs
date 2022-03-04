using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.LogV2;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	//qq rename to 'FirstCharacterHasher' or similar
	class FirstCharacterHasher : ILongHasher<string> {
		public ulong Hash(string x) =>
			(ulong)(x.Length == 0 ? 0 : x[0].GetHashCode());
	}

	// expects stream names of the form
	// "a-stream"
	// "b-$$a-stream" - the metastreams of "a-stream", which hashes to "b"
	class MockMetastreamLookup : IMetastreamLookup<string> {
		private readonly LogV2SystemStreams _m = new();

		public bool IsMetaStream(string streamId) {
			return _m.IsMetaStream(streamId.Substring(2));
		}

		public string MetaStreamOf(string streamId) {
			// not implementable because we would need to specify what it should hash to
			throw new System.NotImplementedException();
		}

		public string OriginalStreamOf(string streamId) {
			return _m.OriginalStreamOf(streamId.Substring(2));
		}
	}
}
