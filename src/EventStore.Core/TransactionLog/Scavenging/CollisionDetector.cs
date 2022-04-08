﻿using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.TransactionLog.Scavenging {
	// add things to the collision detector and it keeps a list of things that collided.
	public class CollisionDetector<T> {
		private static EqualityComparer<T> TComparer { get; } = EqualityComparer<T>.Default;

		// maps from hash to (any) user of that hash
		private readonly IScavengeMap<ulong, T> _hashes;
		private readonly ILongHasher<T> _hasher;

		// store the values. could possibly store just the hashes instead but that would
		// lose us information and it should be so rare that there are any collisions at all.
		//qq for the real implementation make sure adding is idempotent
		// consider whether this should be reponsible for its own storage, or maybe since
		// there will be hardly any collisions we can just read the data out to store it separately
		private readonly IScavengeMap<T, Unit> _collisions;
		
		public CollisionDetector(
			IScavengeMap<ulong, T> hashes,
			IScavengeMap<T, Unit> collisionStorage,
			ILongHasher<T> hasher) {

			_hashes = hashes;
			_collisions = collisionStorage;
			_hasher = hasher;
		}

		public bool IsCollision(T item) => _collisions.TryGetValue(item, out _);
		//qq is this used in a path where allocations are ok.. albeit a pretty small one.
		//would IEnumerable better, consider threading
		public T[] GetAllCollisions() => _collisions
			.Select(x => x.Key)
			.OrderBy(x => x)
			.ToArray();

		// Proof by induction that DetectCollisions works
		// either:
		//   1. we have seen this stream before.
		//       then either:
		//       a. it was a collision the first time we saw it
		//           - by induction (2a) we concluded it was a collision at that time and added it to the
		//             collision list so we conclude it is a collision now
		//             TICK
		//       b. it was not a collision but has since been collided with by a newer stream.
		//           - when it was collided (2a) with we noticed and added it to the collision list so
		//             we conclude it is still a collision now
		//             TICK
		//       c. it was not a collision and still isn't
		//           - so we look in the backing data to see if this hash is in use
		//             it is, because we have seen this stream before, but since this is not a collision
		//             the user of the stream turns out to be us.
		//             conclude no collision
		//             TICK
		//
		//   2. this is the first time we are seeing this stream.
		//       then either:
		//       a. it collides with any stream we have seen before
		//           - so we look in the backing data to see if this hash is in use.
		//             it is, because this is the colliding case.
		//             we look up the record that uses this hash
		//             the record MUST be for a different stream because this is the the first time
		//                we are seeing this stream
		//             collision detected.
		//             add both streams to the collision list. we have their names and the hash.
		//             NB it is possible that we are colliding with multiple streams,
		//                    but they are already in the backing data
		//                    because they are colliding with each other.
		//             TICK
		//       b. it does not collide with any stream we have seen before
		//           - so we look in the backing data to see if there are any entries for this hash
		//             there are not because this is the first time we are seeing this stream and it does
		//                not collde.
		//             conclude no collision, register this stream as the user.
		//             TICK

		// Adds an item and detects if it collides with other items that were already added.
		// collision is only defined when returning NewCollision.
		// in this way we can tell when anything that was not colliding becomes colliding.
		public CollisionResult DetectCollisions(T item, out T collision) {
			if (IsCollision(item)) {
				collision = default;
				return CollisionResult.OldCollision; // previously known collision. 1a or 1b.
				// _hashes must already have this hash, otherwise it wouldn't be a collision.
				// so no need to add it it.
			}

			// collision not previously known, but might be a new one now.
			var itemHash = _hasher.Hash(item);
			if (!_hashes.TryGetValue(itemHash, out collision)) {
				// hash wasn't even in use. it is now.
				_hashes[itemHash] = item;
				return CollisionResult.NoCollision; // hash not in use, can be no collision. 2b
			}

			// hash in use, but maybe by the item itself.
			if (TComparer.Equals(collision, item)) {
				return CollisionResult.NoCollision; // no collision with oneself. 1c
			}

			// hash in use by a different item! found new collision. 2a
			_collisions[item] = Unit.Instance;
			_collisions[collision] = Unit.Instance;
			return CollisionResult.NewCollision;
		}
	}
}