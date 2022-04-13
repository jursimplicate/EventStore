using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq name
	//qq the enumeration is a bit clunky, see how this pans out with the stored version.
	//  the calculator enumerates through the scavengemap to calculate things, each entry it processes
	//  it sets back into the map. maybe it would be better expressed as a visitor.
	//  in any case the enumerator needs to cope with the collection values being updated in this
	//  restricted way as we iterate through.

	//qq apart from the in memory version, we'll probably want a couple of different persistent versions
	// too.
	//  - one that stores a large number of fixed size keys/values with random access
	//      (eg hashes are keys)
	//  - one that stores a small number of variable sized keys (e.g. for stream names)
	//  - one that stores a large number of items where the key is a hash and the value is a stream name
	//      may or may not be a separate implemention on the grounds that the values are variable size
	public interface IScavengeMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> {
		bool TryGetValue(TKey key, out TValue value);
		TValue this[TKey key] { set; }
		//qq we can have void Remove(TKey) if that is easier to implement
		bool TryRemove(TKey key, out TValue value);
		IEnumerable<KeyValuePair<TKey, TValue>> FromCheckpoint(TKey checkpoint);
	}
}
