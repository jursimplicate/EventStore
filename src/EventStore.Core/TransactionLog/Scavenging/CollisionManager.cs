using System;
using System.Collections.Generic;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.TransactionLog.Scavenging {
	// this class efficiently stores/retrieves data against keys that very rarely but sometimes have a
	// hash collision.
	// when there is a hash collision the key is stored explicitly with the value
	// otherwise it only stores the hashes and the values.
	//
	// in practice this allows us to
	//   1. store data for lots of streams with much reduced size and complexity
	//      because we rarely if ever need to store the stream name, and the hashes are fixed size
	//   2. when being read, inform the caller whether the key hash collides or not
	//
	// for retrieval, if you have the key then you can always get the value
	// if you have the hash then what? //qq
	// and you can iterate through everything.


	//qq this is wrapping the 
	//qq rename once it has some shape.
	//
	//qqqq hum now that we want to handle tombstones separately, we will likely want to 
	// share one collision detector between two CollisionManagers.... but then
	// we shouldn't call detect collisions in here at all.
	public class CollisionManager<TKey, TValue> {
		//qq inject the storage here so it can in mem or not
		private readonly IScavengeMap<ulong, TValue> _nonCollisions;
		private readonly IScavengeMap<TKey, TValue> _collisions;
		private readonly ILongHasher<TKey> _hasher;
		private readonly Func<TKey, bool> _isCollision;

		public CollisionManager(
			ILongHasher<TKey> hasher,
			Func<TKey, bool> isCollision,
			IScavengeMap<ulong, TValue> nonCollisions,
			IScavengeMap<TKey, TValue> collisions) {

			_hasher = hasher; //qq slightly suspicious of having to pass the hasher in here
			_isCollision = isCollision;
			_nonCollisions = nonCollisions;
			_collisions = collisions;
		}

		public bool TryGetValue(TKey key, out TValue value) {
			return _collisions.TryGetValue(key, out value)
				|| _nonCollisions.TryGetValue(_hasher.Hash(key), out value);
		}

		//qq it is required that the key we use is already checked for collisions.
		//qq ok, in a nutshell my idea of mapping the streams to their metadata won't work because
		// say we have metdatas for two streams, and the stream names happen to collide, but one of the
		// streams doesn't actually exist, then the index check cant detect the collision, and one
		// streams metadata may overwrite the other.
		// put another way, this violates the requirement that we check the keys for collisions with
		// other keys before using them to store data against here, because we can only check for
		// collisions of things actually present in the log. or put a third way, only names in the log
		// can be used as keys.
		//
		// SO we can't use the stream names as keys in the map - we have to use the metadata stream names
		// will that work? should do. we will key against the metadta streams, because indeed every
		// stream that needs scavenging is associated with a metadata stream. when we scavenge a chunk we
		// can determine the metadata stream for each record we find easily, and see if it exists. i
		// think it's fine.
		public TValue this[TKey key] {
			get {
				if (!TryGetValue(key, out var v))
					throw new KeyNotFoundException(); //qq detail
				return v;
			}

			set {
				//qq do we want isCollision in here or do we want to check it out there
				// and have the sette take a StreamHandle
				if (_isCollision(key)) {
					_collisions[key] = value;
				} else {
					_nonCollisions[_hasher.Hash(key)] = value;
				}
			}
		}

		// when a key that didn't used to be a collision, becomes a collision.
		public void NotifyCollision(TKey key) {
			// this is very rare, so we can just leave the data in _nonCollisions to save having to
			// support removing entries from it. just when reading we need the collisions to take
			// precedence.
			if (_nonCollisions.TryGetValue(_hasher.Hash(key), out var value)) {
				_collisions[key] = value;
			} else {
				//qq we are notified that the key is a collision, but we dont have any entry for it
				// so probably nothing to do
			}
		}

		//qq consider name and whether this wants to be a method, and whether in fact it should return
		// an enumerator or an enumerable
		//qq generalize so that it isn't for streams specifically
		//qq consider if we need to cover writing happening while we enumerate
		public IEnumerable<(StreamHandle<TKey> Handle, TValue Value)> Enumerate() {
			//qq later its possible that we will have calculated this elsewhere already and can
			//just do lookups
			HashSet<ulong> _collidingHashes = null;
			foreach (var kvp in _collisions) {
				yield return (StreamHandle.ForStreamId(kvp.Key), kvp.Value);
				_collidingHashes ??= new();
				_collidingHashes.Add(_hasher.Hash(kvp.Key));
			}

			foreach (var kvp in _nonCollisions) {
				if (_collidingHashes is not null && _collidingHashes.Contains(kvp.Key))
					continue;
				yield return (StreamHandle.ForHash<TKey>(kvp.Key), kvp.Value);
			}
		}
	}
}
