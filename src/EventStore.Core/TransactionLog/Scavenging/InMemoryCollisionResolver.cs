using System;
using System.Collections.Generic;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq taking another run. is this going to end up using the collision resolver, or replacing it.
	//qq obviously rename once it has some shape.
	public class CollisionManager<TKey, TValue> {
		private readonly ILongHasher<TKey> _hasher;
		private readonly CollisionDetector<TKey> _collisionDetector;
		//qq inject as interface to resolver
		private readonly InMemoryCollisionResolver<TKey, TValue> _collisionResolver;

		public CollisionManager(
			ILongHasher<TKey> hasher,
			CollisionDetector<TKey>.HashInUseBefore hashInUseBefore,
			InMemoryCollisionResolver<TKey, TValue> collisionResolver) {

			_hasher = hasher;
			_collisionDetector = new(hashInUseBefore);
			_collisionResolver = collisionResolver;
		}

		public void DetectCollisions(TKey key, long position) {
			//qq if we detect a collision, we might need to promote previous uncollisions.
			// note that we need to do this HERE and not in the set indexer
			if (_collisionDetector.DetectCollisions(key, position, out var collision) == CollisionResult.NewCollision) {
				_collisionResolver.Promote(collision);
			}
		}

		//qq method? property? enumerable? array? clunky allocations at the moment.
		public IEnumerable<TKey> Collisions() {
			return _collisionDetector.GetAllCollisions();
		}

		public bool TryGetValue(TKey key, out TValue value) {
			return _collisionResolver.TryGetValue(key, out value);
		}

		//qq it is required that the key we use is already checked for collisions.
		//qq ok, in a nutshell my idea of mapping the streams to their metadata won't work because
		// say we have metdatas for two streams, and the stream names happen to collide, but one of the streams
		// doesn't actually exist, then the index check cant detect the collision, and one streams metadata
		// may overwrite the other.
		// put another way, this violates the requirement that we check the keys for collisions with other keys
		// before using them to store data against here, because we can only check for collisions of things
		// actually present in the log. or put a third way, only names in the log can be used as keys.
		//
		// SO we can't use the stream names as keys in the map - we have to use the metadata stream names.
		// will that work? should do. we will key against the metadta streams, because indeed every stream
		// that needs scavenging is associated with a metadata stream. when we scavenge a chunk we can
		// determine the metadata stream for each record we find easily, and see if it exists. i think it's fine.
		//
		//qqqqqqq HMM!!!!! but that isn't the case if we are keying the metadata against the _original_ stream.
		// choices
		//  - key against the metadata stream instead (somewhere i wrote that this is 1-1 match but it isn't -
		//        you can have metadata for a stream that doesn't exist)
		//  - check the original stream name for collisions when we find the metadata (but what do we put as the 
		//        position? if we use metadata position is that cheating?
		//            i think it is cheating because we might have a metadata each for two streams that dont exist
		//              $$a and $$b and it just so happens that a and b hash to the same value
		//              then we do need to store the metadata in the map against the names not the hashe
		//              (otherwise they will overwrite each others metadata)
		//              which means we do need to realise that they collide.
		//              but we wont relase that they collide because they aren't actualyl in the log
		//              unless when we check hashinusebefore for a metadata stream we also check it for the original stream
		//              
		public TValue this[TKey key] {
			get {
				if (!TryGetValue(key, out var v))
					throw new KeyNotFoundException(); //qq detail
				return v;
			}

			//qq this is pretty clunky
			set {
				//qq consider whether we should add to the collision detector here (would need the position)
				// or just check it, it having been added earlier.
				// consider threading.
				//
				// backing up. we are progressing through a log, spottling collisions and
				// building a current state 
				// ah but, when do we promote, is it as soon as thre is a collision, or only later
				// when we try to update some element of the map here.
				// it probably has to be when the collision is discovered, at least, before we allow
				// the checkpoint to be updated.
				// we won't know which 
				//

				//qq consider whether we should check existence first
				if (_collisionDetector.IsCollision(key)) {
					//qq need to promote
					//					var collisions = _collisionDetector.GetAllCollisions();

					_collisionResolver.Set(key, value);
				} else {
					_collisionResolver.Set(_hasher.Hash(key), value);
				}
			}
		}

		public IEnumerable<(StreamHandle<TKey> Handle, TValue Value)> Enumerate() {
			return _collisionResolver.Enumerate();
		}
	}

		//qq name.
		// this class efficiently stores/retrieves data that very rarely but sometimes has a hash collision.
		// when there is a hash collision the key is stored explicitly with the value
		// otherwise it only stores the hashes and the values.
		//
		// for retrieval, if you have the key then you can always get the value
		// if you have the hash then what? //qq
		// and you can iterate through everything.
		//
		//qqqqqq in fact, for now, perhaps we don't need to bother storing values against the collisions
		// here at all - we can just not bother scavenging those streams?
		//    at some point we will need it for gdpr, or in case a really big stream collides
		//    so really we had better at least sketch out the implementation to be sure it will work.
		//
		//qq resolver might not be the right name now... but this is still the backing store,
		// storing the values efficiently in two places. but without the logic to drive it.
		public class InMemoryCollisionResolver<TKey, TValue> {
		private readonly Dictionary<ulong, TValue> _nonCollisions = new();
		private readonly Dictionary<TKey, TValue> _collisions = new();
		private readonly ILongHasher<TKey> _hasher;

		public InMemoryCollisionResolver(
			ILongHasher<TKey> hasher) {

			_hasher = hasher;

			//qq need a hasher? or maybe need a collision detector


			//qq consider that what was not a collision can be collided with.
			// do we need to move it at that point? say there are three streams that collide
			// together, could we tell which one was the one in the uncollided map
			// or would we need to store all three in the collisions.
			//qqqqq could we write a test that would fail if we did this?
			//qqqqq BUT if it is collided with later, we need to get its key to promote it
			// like the collision detector does.

			//qq definitely need a way to persist the non-collisions nicely
		}

		// when a key that didn't used to be a collision, becomes a collision.
		public void Promote(TKey key) {
			//qq this should be so rare that we could just leave it in the _nonCollisions structure
			// and just be careful to exclude it from the enumeration
			if (_nonCollisions.Remove(_hasher.Hash(key), out var value))
				_collisions[key] = value;
		}

		public void Set(TKey key, TValue value) {
			_collisions[key] = value;
		}

		public void Set(ulong hash, TValue value) {
			_nonCollisions[hash] = value;
		}

		public bool TryGetValue(TKey key, out TValue value) {
			return _collisions.TryGetValue(key, out value)
				|| _nonCollisions.TryGetValue(_hasher.Hash(key), out value);
		}

		//qq consider name and whether this wants to be a method, and whether in fact it should return
		// an enumerator or an enumerable
		//qq generalize so that it isn't for streams specifically
		public IEnumerable<(StreamHandle<TKey>, TValue)> Enumerate() {
			foreach (var kvp in _collisions)
				yield return (StreamHandle.ForStreamId(kvp.Key), kvp.Value);

			foreach (var kvp in _nonCollisions)
				yield return (StreamHandle.ForHash<TKey>(kvp.Key), kvp.Value);
		}
	}
}
