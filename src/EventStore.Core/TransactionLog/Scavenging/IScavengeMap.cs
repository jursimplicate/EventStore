using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq name
	//qq the enumeration is a bit clunky, see how this pans out with the stored version.
	public interface IScavengeMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> {
		bool TryGetValue(TKey key, out TValue value);
		TValue this[TKey key] { set; }
	}
}
