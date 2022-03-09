using System;
using System.Collections.Generic;
using System.Linq;

namespace EventStore.Core.TransactionLog.Scavenging {
	// add things to the collision detector and it keeps a list of things that collided.
	public class CollisionDetector<T> {
		// checks if the hash is in use before this item at this position. returns true if so.
		// if returning true then out parameter is one of the items that hashes to that hash
		//qq why is it a candidateColliddee exactly, i think (check?) because it might be 'itself'
		// in which case it isn't a collision. it would be better to be called hashUser
		public delegate bool HashInUseBefore(T item, long itemPosition, out T candidateCollidee);

		private static EqualityComparer<T> TComparer { get; } = EqualityComparer<T>.Default;
		private readonly HashInUseBefore _hashInUseBefore;

		// store the values. could possibly store just the hashes instead but that would
		// lose us information and it should be so rare that there are any collisions at all.
		//qq for the real implementation make sure adding is idempotent
		// consider whether this should be reponsible for its own storage, or maybe since
		// there will be hardly any collisions we can just read the data out to store it separately
		private readonly IScavengeMap<T, Unit> _collisions;
		
		//qq will need something like this to tell where to continue from. maybe not in this class though
		private long _lastPosition;

		public CollisionDetector(
			HashInUseBefore hashInUseBefore,
			IScavengeMap<T, Unit> collisionStorage) {

			_hashInUseBefore = hashInUseBefore;
			_collisions = collisionStorage;
		}

		public bool IsCollision(T item) => _collisions.TryGetValue(item, out _);
		//qq is this used in a path where allocations are ok.. albeit a pretty small one.
		//would IEnumerable better, consider threading
		public T[] GetAllCollisions() => _collisions
			.Select(x => x.Key)
			.OrderBy(x => x)
			.ToArray();

		// allllright. when we are adding an item, either:
		//   1. we have seen this stream before.
		//       then either:
		//       a. it was a collision the first time we saw it
		//           - by induction we concluded it was a collision at that time and added it to the
		//             collision list so we conclude it is a collision now
		//             TICK
		//       b. it was not a collision but has since been collided with by a newer stream.
		//           - when it was collided with we noticed and added it to the collision list so
		//             we conclude it is still a collision now
		//             TICK
		//             NB: it is important that we added this stream to the collision list. consider:
		//                 if we look in the index to see if there are any entries for this hash
		//                 there would be, because this is the collision case
		//                 and, crucially, those entries would be a mix of this stream and other streams
		//                 because this is the 'seen before' case.
		//                 so we couldn't tell by looking up a single entry whether this was a collision
		//                 or not. BAD.
		//       c. it was not a collision and still isn't
		//           - so we look in the index to see if there are any entries for this hash
		//             in the log _before_ this item.
		//             there are, because we have seen this stream before
		//             all the entries are for this stream, but we only want to check one.
		//             so we check one, and we find that it is for this stream
		//             conclude no collision
		//             TICK
		//
		//   2. this is the first time we are seeing this stream.
		//       then either:
		//       a. it collides with any stream we have seen before
		//           - so we look in the index to see if there are any entries for this hash
		//             in the log _before_ this item.
		//             there are, because this is the colliding case.
		//             we pick any entry and look up the record
		//             the record MUST be for a different stream because this is the the first time
		//                we are seeing this stream and we carefully excluded ourselves from the
		//                index search. (if we didn't do that then we could have found a record for
		//                our own stream here and not been able to distinguish this case from 1c
		//                based on looking up a single record.
		//             collision detected.
		//             add both streams to the collision list. we have their names and the hash.
		//             BUT is it possible that we are colliding with multiple streams, do we need to add
		//                    them all to the hash? it is possible yes, but they are already in the hash
		//                    because they are colliding with each other.
		//             TICK
		//       b. it does not collide with any stream we have seen before
		//           - so we look in the index to see if there are any entries for this hash
		//             there are not because this is the first time we are seeing this stream and it does
		//                not collde.
		//             conclude no collision
		//             TICK


		//qqqqqqqqqqqqq consider the case that the records that the index references have been scavenged
		// all of them? some of them?
		// perhaps start with lookin up the largest event number cause its most likely to still be there
		// but what if it isn't??
		//   maybe we just try to load records one by one working backwards until we find one that still
		//   exists (log a warning too probably because this will be a problem for performance)
		//   and if none of them exist then thats the same as if a previous scavenge completed
		//   and removed the entries from the index too.
		//   for that matter, what if the scavenge has completed and removed the entries from the index too
		//     maybe it will be ok, we won't register it as a collision yet but we will later if/when we
		//     find the remaining events. but what if the last event is beyond the scavenge point, would
		//     that be bad?

		// Adds an item from the given position. Detects if it collides with other items prior to that
		// position.
		// collision is only defined when returning NewCollision.
		// in this way we can tell when anything that was not colliding becomes colliding.
		public CollisionResult DetectCollisions(T item, long itemPosition, out T collision) {
			_lastPosition = itemPosition;

			if (IsCollision(item)) {
				collision = default;
				return CollisionResult.OldCollision; // previously known collision. 1a or 1b.
			}

			// collision not previously known, but might be a new one now.
			if (!_hashInUseBefore(item, itemPosition, out collision)) {
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
