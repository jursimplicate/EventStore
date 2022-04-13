//qq implement.consider whether decorator that adds a bloom filter would be useful as well as
// caching in certain cases too.
using System.Collections;
using System.Collections.Generic;
using EventStore.Core.DataStructures;

namespace EventStore.Core.TransactionLog.Scavenging {
	// All access to the wrapped map must be via the cache.
	public class LruCachingScavengeMap<TKey, TValue> : IScavengeMap<TKey, TValue> {
		private readonly LRUCache<TKey, TValue> _cache;
		private readonly IScavengeMap<TKey, TValue> _wrapped;

		public LruCachingScavengeMap(IScavengeMap<TKey, TValue> wrapped, int cacheMaxCount) {
			_wrapped = wrapped;
			_cache = new LRUCache<TKey, TValue>(cacheMaxCount);
		}

		public TValue this[TKey key] {
			set {
				_wrapped[key] = value;
				_cache.Put(key, value);
			}
		}

		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
			_wrapped.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public IEnumerable<KeyValuePair<TKey, TValue>> FromCheckpoint(TKey checkpoint) =>
			_wrapped.FromCheckpoint(checkpoint);

		public bool TryGetValue(TKey key, out TValue value) {
			if (_cache.TryGet(key, out value))
				return true;

			if (_wrapped.TryGetValue(key, out value)) {
				_cache.Put(key, value);
				return true;
			}

			//qq consider if a lrucache of keys that are known not to exist would be helpful
			//for our use cases.
			return false;
		}

		public bool TryRemove(TKey key, out TValue value) {
			_cache.Remove(key);
			return _wrapped.TryRemove(key, out value);
		}
	}
}
