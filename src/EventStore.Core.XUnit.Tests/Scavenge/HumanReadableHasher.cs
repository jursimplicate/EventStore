using EventStore.Core.Index.Hashes;
using EventStore.Core.Services;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	//qq finish implementations
	// Generates hashes that are obvious to humans based on the stream name.
	// The first character of the stream name is the basis of the hash for the corresponding metastream
	// The second character of the stream name is the basis of the hash for the original stream
	// e.g.
	//   "$$ma-1 -> #m
	//   "ma-1" -> #a
	class HumanReadableHasher : ILongHasher<string> {
		public ulong Hash(string x) {
			if (x == "")
				return 0;

			var c = SystemStreams.IsMetastream(x)
				? x[2]
				: x[1];

			return (ulong)c.GetHashCode();
		}
	}
}
