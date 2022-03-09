using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq the purpose of this datastructure is to narrow the scope of what needs to be
	// calculated based on what we can glean by tailing the log,
	// without doubling up on what we can easily look up later.
	public interface IScavengeState<TStreamId> :
		IScavengeStateForAccumulator<TStreamId>,
		IScavengeStateForCalculator<TStreamId>,
		IScavengeStateForIndexExecutor<TStreamId>,
		IScavengeStateForChunkExecutor<TStreamId> {
	}

	//qq this summary comment will explain the general shape of the scavenge state - what it needs to be
	// able to store and retrieve and why.
	//
	// There are two kinds of streams that we might want to remove events from
	//    - original streams
	//        - according to tombstone
	//        - according to metadata (maxage, maxcount, tb)
	//    - metadata streams
	//        - according to tombstone
	//        - maxcount 1
	//
	// Together these are the scavengable streams. We store a DiscardPoint for each so that we can
	// 
	// We only need to store metadata for the user streams with metadata since the metadata for
	// metadatastreams is implicit.
	// 
	// however, we need to know about _all_ the stream collisions in the database not just the ones
	// that we might remove events from, so that later we can scavenge the index without looking anything
	// up in the log.


	// accumulator iterates through the log, spotting metadata records
	// put in the data that the chunk and ptable scavenging require
	public interface IScavengeStateForAccumulator<TStreamId> {
		// call this for each record as we accumulate through the log so that we can spot every hash
		// collision to save ourselves work later.
		//qq maybe prefer passing in a single arg (the record) and getting its streamid and position
		//qq mabe rename if we want the state to not be containing logic
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

		void SetOriginalStreamData(TStreamId streamId, DiscardPoint discardPoint);
	}

	//qqqq this might _be_ IScavengeState. perhaps rename it
	public interface IScavengeStateForCalculator<TStreamId> {
		// Calculator iterates through the scavengable streams and their metadata
		//qq note we dont have to _store_ the metadatas for the metadatastreams internally, we could
		// store them separately. (i think i meant e.g. store their address in the log)
		IEnumerable<(StreamHandle<TStreamId> MetadataStreamHandle, MetastreamData)> MetastreamDatas { get; }

		DiscardPoint GetOriginalStreamData(StreamHandle<TStreamId> streamHandle);
		//qq we set a discard point for every relevant stream.
		void SetOriginalStreamData(StreamHandle<TStreamId> streamHandle, DiscardPoint discardPoint);
	}



	// Then the executor:

	public interface IScavengeStateForChunkExecutor<TStreamId> {
		IEnumerable<IReadOnlyChunkScavengeInstructions<TStreamId>> ChunkInstructionss { get; }
		DiscardPoint GetDiscardPoint(TStreamId streamId);
	}

	public interface IScavengeStateForIndexExecutor<TStreamId> {
		//qq needs to work for metadata streams and also for 
		bool IsCollision(ulong streamHash);
		DiscardPoint GetDiscardPoint(StreamHandle<TStreamId> streamHandle);
	}

	//qq note, probably need to complain if the ptable is a 32bit table
	//qq maybe we need a collision free core that is just a map
	//qq just a thought, lots of the metadatas might be equal, we might be able to store each uniqueb
	// instance once. implementation detail.
}
