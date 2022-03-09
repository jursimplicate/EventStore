using System.Collections;
using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class InMemoryScavengeMap<TKey, TValue> : IScavengeMap<TKey, TValue> {
		private readonly Dictionary<TKey, TValue> _dict = new();

		public TValue this[TKey key] {
			set => _dict[key] = value;
		}

		public bool TryGetValue(TKey key, out TValue value) => _dict.TryGetValue(key, out value);

		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
