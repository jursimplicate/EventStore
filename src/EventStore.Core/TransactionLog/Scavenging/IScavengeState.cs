using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
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

		// call this to record what the current metadata is in a metadata stream.
		// if there is previous metadata this will just overwrite it.
		MetastreamData GetMetastreamData(TStreamId streamId);
		void SetMetastreamData(TStreamId streamId, MetastreamData streamData);

		void SetOriginalStreamData(TStreamId streamId, DiscardPoint discardPoint);
	}

	public interface IScavengeStateForCalculator<TStreamId> {
		// Calculator iterates through the scavengable streams and their metadata
		//qq note we dont have to _store_ the metadatas for the metadatastreams internally, we could
		// store them separately. (i think i meant e.g. store their address in the log)
		IEnumerable<(StreamHandle<TStreamId> MetadataStreamHandle, MetastreamData)> MetastreamDatas { get; }

		DiscardPoint GetOriginalStreamData(StreamHandle<TStreamId> streamHandle);
		//qq we set a discard point for every relevant stream.
		void SetOriginalStreamData(StreamHandle<TStreamId> streamHandle, DiscardPoint discardPoint);
	}

	//qq needs to work for metadata streams and also for original streams
	// but that is easy enough because we can see if the streamid is for a metastream or not
	public interface IScavengeStateForChunkExecutor<TStreamId> {
		IEnumerable<IReadOnlyChunkScavengeInstructions<TStreamId>> ChunkInstructionss { get; }
		DiscardPoint GetDiscardPoint(TStreamId streamId);
	}

	//qq needs to work for metadata streams and also for original streams
	//qq which is awkward because if we only have the hash we don't know which it is
	// we would need to check in both maps which is not ideal.
	//qq ^ the index executor should be smart enough though to only call this once per non-colliding stream.
	public interface IScavengeStateForIndexExecutor<TStreamId> {
		bool IsCollision(ulong streamHash);
		DiscardPoint GetDiscardPoint(StreamHandle<TStreamId> streamHandle);
	}
}
