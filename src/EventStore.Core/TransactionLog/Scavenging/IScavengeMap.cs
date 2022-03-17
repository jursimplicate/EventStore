using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq name
	//qq the enumeration is a bit clunky, see how this pans out with the stored version.

	//qq apart from the in memory version, we'll probably want a couple of different persistent versions
	// too.
	//  - one that stores a large number of fixed size keys/values with random access
	//      (eg hashes are keys)
	//  - one that stores a small number of variable sized keys (e.g. for stream names)
	//  - we might want one that stores contiguous keys (eg chunk numbers)
	public interface IScavengeMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> {
		bool TryGetValue(TKey key, out TValue value);
		TValue this[TKey key] { set; }
	}
}
