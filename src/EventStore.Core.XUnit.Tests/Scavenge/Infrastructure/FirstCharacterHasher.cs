using EventStore.Core.Index.Hashes;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	class FirstCharacterHasher : ILongHasher<string> {
		public ulong Hash(string x) =>
			(ulong)(x.Length == 0 ? 0 : x[0].GetHashCode());
	}
}
