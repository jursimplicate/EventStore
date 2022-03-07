using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	public readonly struct DiscardPoint {
		//qq do we need as many bits as this
		public readonly long Value;

		//qq probably make this private and use static method that makes the meaning clear
		public DiscardPoint(long value) {
			Value = value;
		}

		//qq make sure this is correct wrt the semantics of the Value
		public static readonly DiscardPoint Tombstone = new(long.MaxValue);

		//qq get this right wrt inclusive/exclusive. name? DiscardBefore ? 
		public static DiscardPoint DiscardBefore(long value) {
			return new(value);
		}

		public static DiscardPoint DiscardIncluding(long value) {
			//qq if this is in range
			return DiscardBefore(value + 1);
		}
		// Produces a discard point that discards when any of the provided discard points would discard
		// i.e. takes the bigger of the two.
		public static DiscardPoint AnyOf(DiscardPoint x, DiscardPoint y) {
			return x.Value > y.Value ? x : y;
		}

		//qq property? consider name and semantics
		public bool IsNotMax() {
			return Value < long.MaxValue;
		}

		//qq depends on whether the DP is the first event to keep
		// or the last event to discard. which in turn will depend on which is easier to generate
		// consider the edges of the range actually. tombstone is long.max and usually we would/
		// keep the tombstone but there is an option to discard it, in which case the value should
		// presumably be long.max and it should be the last event to discard.
		// we also need a way to express that we want to keep all the events. 
		//qq we do need to have a discard point that means 'discard nothing'
		public bool ShouldDiscard(long eventNumber) =>
			eventNumber < Value;
	}

	//qq prolly dont need these once the dust settles
	public readonly struct StreamHash {
		public readonly ulong Value;
	}

	public readonly struct StreamName {
		public readonly string Value;
	}

	// there are three kinds of streams that we might want to remove events from
	//    - User streams with metadata.
	//    - Metadata streams.
	//    - streams with tombstones
	//
	// however, we need to know about _all_ the stream collisions in the database not just the ones
	// that we might remove events from, so that later we can scavenge the index without looking anything
	// up in the log.

	// Together these are the scavengable streams. We need a DiscardPoint for each.
	// We only need to store metadata for the user streams with metadata since the metadata for
	// metadatastreams is implicit.

	// accumulator iterates through the log, spotting metadata records
	// put in the data that the chunk and ptable scavenging require
	public interface IMagicForAccumulator<TStreamId> {
		// call this for each record as we accumulate through the log so that we can spot every hash
		// collision to save ourselves work later.
		//qq maybe prefer passing in a single arg (the record) and getting its streamid and position
		void NotifyForCollisions(TStreamId streamId, long position);

		//qq the API here can either expose a way for the accumulator to get and set the stream data
		// OR it can provide a way to just be told there is new metadata or tombstones and it can
		// update itself. for now run with the former because
		//    1. accumulator can drive, map can just be a datastructure
		//    2. map probably has to provide an api to read _anyway_
		// call this to record what the current metadata is for a stream.
		// if there is previous metadata this will just overwrite it.
		//
		// this needs to spot if there is a hash collision. can it do it? bear in mind that it can be
		// called multiple times for the same stream.
		// 1. hash the stream name, see if we already have a record for that stream.
		//         (check the (hash -> name) cache first) (check the collisions first also?)
		//    - if we do have a record for the hash
		//        - if that record has the same stream name
		//            - means we need to store the address of the metadata record to get the name
		//            - (make sure we dont scavenge that record while referencing it.. or if we legit can,
		//               that something sensible happens)
		//            - just update it. no collision.
		//            - (note we can cache the (hash -> stream name) lookup, populate it as we call Set as
		//               well)
		//        - else (different stream name)
		//            - collision detected!
		//            - store the streamName and streamData in another datastructure (collision structure)
		//            - do we need to pull the one that got collided with out into the collision structure?
		//                - we can if we need to because we have both stream names and stream datas here.
		//                  that will do for now.
		//    - else (no record)
		//        - just add it
		MetastreamData GetMetastreamData(TStreamId streamId);
		void SetMetastreamData(TStreamId streamId, MetastreamData streamData);

		//qqqqqqq we already have a method to set the discard point, maybe we dont need these in addition
		// although perhaps setting the discard point should be renamed to 'AdvanceDiscardPoint'
		OriginalStreamData GetOriginalStreamData(TStreamId streamId);
		void SetOriginalStreamData(TStreamId streamId, OriginalStreamData streamData);
	}

	//qqqq this might _be_ IScavengeState. perhaps rename it
	public interface IMagicForCalculator<TStreamId> {
		// Calculator iterates through the scavengable streams and their metadata
		//qq note we dont have to _store_ the metadatas for the metadatastreams internally, we could
		// store them separately. (i think i meant e.g. store their address in the log)
		IEnumerable<(StreamHandle<TStreamId> MetadataStreamHandle, MetastreamData)> MetastreamDatas { get; }

		DiscardPoint GetDiscardPoint(StreamHandle<TStreamId> streamHandle);
		//qq we set a discard point for every relevant stream.
		void SetDiscardPoint(StreamHandle<TStreamId> streamHandle, DiscardPoint discardPoint);
	}



	// Then the executor:

	public interface IMagicForExecutor {
		bool IsCollision(StreamHash streamHash);
		DiscardPoint GetDiscardPoint(StreamName streamName);
		DiscardPoint GetDiscardPoint(StreamHash streamHash);
	}

	//qq note, probably need to complain if the ptable is a 32bit table
	//qq maybe we need a collision free core that is just a map
	//qq just a thought, lots of the metadatas might be equal, we might be able to store each uniqueb
	// instance once. implementation detail.
}
