using System;
using System.Collections.Generic;
using EventStore.Core.Data;

namespace EventStore.Core.TransactionLog.Scavenging {
	public interface IScavengeState<TStreamId> :
		IScavengeStateForAccumulator<TStreamId>,
		IScavengeStateForCalculator<TStreamId>,
		IScavengeStateForIndexExecutor<TStreamId>,
		IScavengeStateForChunkExecutor<TStreamId> {

		bool TryGetCheckpoint(out ScavengeCheckpoint checkpoint);
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
	public interface IScavengeStateForAccumulator<TStreamId> : IScavengeStateCommon {
		// call this for each record as we accumulate through the log so that we can spot every hash
		// collision to save ourselves work later.
		void DetectCollisions(TStreamId streamId);

		void SetMetastreamDiscardPoint(TStreamId streamId, DiscardPoint discardPoint);

		void SetOriginalStreamMetadata(TStreamId originalStreamId, StreamMetadata metadata);
		void SetOriginalStreamTombstone(TStreamId streamId);

		void SetChunkTimeStampRange(int logicalChunkNumber, ChunkTimeStampRange range);
	}

	public interface IScavengeStateForCalculatorReadOnly<TStreamId> : IScavengeStateCommon {
		// Calculator iterates through the scavengable original streams and their metadata
		// it doesn't need to do anything with the metadata streams, accumulator has done those.
		//qq name
		IEnumerable<(StreamHandle<TStreamId>, OriginalStreamData)> OriginalStreamsToScavenge(
			StreamHandle<TStreamId> checkpoint);

		bool TryGetChunkTimeStampRange(int logicaChunkNumber, out ChunkTimeStampRange range);
	}

	public interface IScavengeStateForCalculator<TStreamId> :
		IScavengeStateForCalculatorReadOnly<TStreamId> {

		void SetOriginalStreamDiscardPoints(
			StreamHandle<TStreamId> streamHandle,
			DiscardPoint discardPoint,
			DiscardPoint maybeDiscardPoint);

		void IncreaseChunkWeight(int logicalChunkNumber, float extraWeight);
	}

	public interface IScavengeStateForChunkExecutor<TStreamId> : IScavengeStateCommon {
		bool TryGetChunkWeight(int logicalChunkNumber, out float weight);
		void ResetChunkWeight(int logicalChunkNumber);
		bool TryGetStreamExecutionDetails(TStreamId streamId, out StreamExecutionDetails details);
		bool TryGetMetastreamDiscardPoint(TStreamId streamId, out DiscardPoint discardPoint);
	}

	public interface IScavengeStateForIndexExecutor<TStreamId> : IScavengeStateCommon {
		bool IsCollision(ulong streamHash);
		//qq precondition: the streamhandle must be of the correct kind.
		bool TryGetDiscardPoint(
			StreamHandle<TStreamId> streamHandle,
			out DiscardPoint discardPoint);
	}

	// all the components use these
	public interface IScavengeStateCommon {
		void SetCheckpoint(ScavengeCheckpoint checkpoint);

		//qqqqqqq not sure about these, expose to the components, or let the scavenge state handle them internally.
		//void BeginTransaction();
		//void CommitTransaction();
	}
}
