using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Scavenging {
	// There are two kinds of streams that we might want to remove events from
	//    - original streams (i.e. streams with metadata)
	//        - according to tombstone
	//        - according to metadata (maxage, maxcount, tb)
	//    - metadata streams
	//        - according to tombstone
	//        - maxcount 1
	//
	// In a nutshell:
	// - The Accumulator passes through the log once (in total, not per scavenge)
	//   accumulating state that we need and calculating some DiscardPoints.
	// - When a ScavengePoint is set, the Calculator uses it to finish calculating
	//   the DiscardPoints.
	// - The Chunk and Index Executors can then use this information to perform the
	//   actual record/indexEntry removal.

	public interface IScavenger {
		//qq probably we want this to continue a previous scavenge if there is one going,
		// or start a new one otherwise.
		void Start();
		//qq options
		// - timespan, or datetime to autostop
		// - chunk to scavenge up to
		// - effective 'now'
		// - remove open transactions : bool

		//qq probably we want this to pause a scavenge if there is one going,
		// otherwise probably do nothing.
		// in this way the user sticks with the two controls that they had before: start and stop.
		void Stop();
	}

	// The Accumulator reads through the log up to the scavenge point
	// its purpose is to do any log scanning that is necessary for a scavenge _only once_
	// accumulating whatever state is necessary to avoid subsequent scans.
	//
	// in practice it populates the scavenge state with:
	//  1. the scavengable streams
	//  2. hash collisions between any streams
	//  3. most recent metadata
	//  4. discard points according to TombStones, MetadataMaxCount1, TruncateBefore.
	//qq 5. data for maxage calculations - maybe that can be another IScavengeMap
	public interface IAccumulator<TStreamId> {
		void Accumulate(ScavengePoint scavengePoint, IScavengeStateForAccumulator<TStreamId> state);
	}

	// The Calculator calculates the DiscardPoints that depend on the ScavengePoint
	// (after which all the scavengable streams will have discard points calculated correctly)
	//
	// It also creates a heuristic for which chunks are most in need of scavenging.
	//  - an approximate count of the number of records to discard in each chunk
	//qqqqqqqq logical chunk or physical chunk
	//
	// The job of calculating the DiscardPoints is split between the Accumulator and the Calculator.
	// Some things affect the DiscardPoints in a fairly static way and can be applied to the
	// DiscardPoint directly in the Accumulator. Others set criteria for the DiscardPoint that cause
	// the DiscardPoint to move regularly. For these latter one we delay applying their effect to the
	// DiscardPoint until the calculator, to save us updating them over and over.
	//
	//  - Tombstone : Accumulator
	//  - Static Metadata MaxCount 1 : Accumulator
	//  - Metadata TruncateBefore : Accumulator //qq todo
	//  - Metadata MaxCount : Calculator
	//  - Metadata MaxAge : Calculator
	//
	// We don't account for MaxCount in the Accumulator because every new event would cause the
	// DiscardPoint to Move. (Apart from the MaxCount1 of metadata records, since it has to persist
	// data because of the record anyway)
	//
	// We don't account for MaxAge in the Accumulator because we need the ScavengePoint to define
	// a time to calculate the age from.
	//
	// For streams that do not collide (which is ~all of them) the calculation can be done index-only -
	// that is, without hitting the log at all.
	public interface ICalculator<TStreamId> {
		void Calculate(ScavengePoint scavengePoint, IScavengeStateForCalculator<TStreamId> source);
	}

	// the chunk executor performs the actual removal of the log records
	// should be very rare to do any further lookups at this point.
	public interface IChunkExecutor<TStreamId> {
		void Execute(IScavengeStateForChunkExecutor<TStreamId> instructions);
	}

	// the index executor performs the actual removal of the index entries
	// should be very rare to do any further lookups at this point.
	public interface IIndexExecutor<TStreamId> {
		void Execute(IScavengeStateForIndexExecutor<TStreamId> instructions);
	}


	public interface IChunkManagerForScavenge {
		TFChunk SwitchChunk(TFChunk chunk, bool verifyHash, bool removeChunksWithGreaterNumbers);
		TFChunk GetChunk(int logicalChunkNum);
	}

	//qq there are a couple of places we need to read chunks.
	// 1. during accumulation we need the metadata records and the timestamp of the first record in the
	//    chunk. i wonder if we should use the bulk reader.
	//qq note dont use allreader to implement this, it has logic to deal with transactions, skips
	// epochs etc.
	public interface IChunkReaderForAccumulation<TStreamId> {
		IEnumerable<RecordForAccumulator<TStreamId>> Read(
			int startFromChunk,
			ScavengePoint scavengePoint);
	}

	//qq could use streamdata? its a class though
	public abstract class RecordForAccumulator<TStreamId> {
		//qq make sure to recycle these.
		//qq prolly have readonly interfaces to implement, perhaps a method to return them for reuse
		//qq some of these are pretty similar, wil lthey end up being different in the end
		public class EventRecord : RecordForAccumulator<TStreamId> {
			public TStreamId StreamId { get; set; }
			public long LogPosition { get; set; }
		}

		//qq how do we tell its a tombstone record, detect and abort if the tombstone is in a transaction
		// if thats even possible
		public class TombStoneRecord : RecordForAccumulator<TStreamId> {
			public TStreamId StreamId { get; set; }
			public long LogPosition { get; set; }
		}

		//qq make sure we only instantiate these for metadata records in metadata streams
		// or maybe rather check that this is the case in the Accumulator
		public class MetadataRecord : RecordForAccumulator<TStreamId> {
			public TStreamId StreamId { get; set; }
			public long LogPosition { get; set; }
		}
	}

	// 2. during calculation we want to know the record sizes to determine space saving.
	//      unless we just skip this and approximate it with a record count.
	// 3. when scavenging a chunk we need to read records out of it any copy
	//    the ones we are keeping into the new chunk
	public interface IChunkReaderForScavenge<TStreamId> {
		IEnumerable<RecordForScavenge<TStreamId>> Read(TFChunk chunk);
	}

	// when scavenging we dont need all the data for a record
	//qq but we do need more data than this
	// but the bytes can just be bytes, in the end we are going to keep it or discard it.
	//qq recycle this record like the recordforaccumulation?
	public class RecordForScavenge<TStreamId> {
		public TStreamId StreamId { get; set; }
		public long EventNumber { get; set; }
	}







	//qq this contains enough information about what needs to be removed from each
	// chunk that we can decide whether to scavenge each one (based on some threshold)
	// or leave it until it has more junk in.
	// in order to figure out how much will be scavenged we probably had to do various
	// lookups. expect that we will probably may as well preserve that information so
	// that the execution itself can be done quickly, prolly without additional lookups
	//
	//qqq this is now IStateForChunkExecutor
	//public interface IScavengeInstructions<TStreamId> {
	//	//qqqqq is chunknumber the logical chunk number?
	//	//qq do we want to store a separate file per logical chunk or per physical (merged) chunk.
	//	IEnumerable<IReadOnlyChunkScavengeInstructions<TStreamId>> ChunkInstructionss { get; }
	//	//qq this isn't quite it, prolly need stream name
	//	bool TryGetDiscardPoint(TStreamId streamId, out DiscardPoint discardPoint);
	//}

	// instructions (see above) for scavenging a particular chunk.
	public interface IReadOnlyChunkScavengeInstructions<TStreamId> {
		int ChunkNumber { get; } //qq logical or phsyical?

		//qq int or long? necessarily bytes or rather accumulated weight, or maybe it can jsut be approx.
		// maybe just event count will be sufficient if it helps us to not look up records
		// currently we have to look them up anyway for hash collisions, so just run with that.
		// later we may switch to record count if it helps save lookups - or the index may even be able
		// to imply the size of the record (approximately?) once we have the '$all' stream index.
		int NumRecordsToDiscard { get; }
	}

	//qq consider if we want to use this readonly pattern for the scavenge instructions too
	public interface IChunkScavengeInstructions<TStreamId> :
		IReadOnlyChunkScavengeInstructions<TStreamId> {
		// we call this for each event that we want to discard
		// probably it is better to list what we want to discard rather than what we want to keep
		// because in a well scavenged log we will want to keep more than we want to remove
		// in a typical scavenge.

		//qq now that we dont have args here, perhaps it should be called 'increment' or similar
		void Discard();
	}

	public interface IIndexReaderForAccumulator<TStreamId> {
		//qq definitely a similar here to the delegate defined by the collision detector..
		// is it actually the same thing in need of a refactor? then the other is just a
		// decorator pattern that adds memoisation. might need to pass the hash into
		// collisiondetector.add, or let it hash it itself
		bool HashInUseBefore(ulong hash, long postion, out TStreamId hashUser);
	}


	public readonly struct EventInfo {
		public readonly long LogPosition;
		public readonly long EventNumber;
	}
	//qq name
	public interface IIndexForScavenge<TStreamId> {
		//qq maxposition  / positionlimit instead of scavengepoint?
		//qq better name than 'stream'...
		long GetLastEventNumber(StreamHandle<TStreamId> stream, long scavengePoint);

		//qq name min age or maxage or 
		//long GetLastEventNumber(TStreamId streamId, DateTime age);

		//qq maybe we can do better than allocating an array for the return
		//qqqqq should take a scavengepoint/maxpos?
		//qq better name that indicates that this doesn't return the events - 
		// in fact it doesn't usually touch the log at all
		EventInfo[] ReadEventInfoForward(
			StreamHandle<TStreamId> stream,
			long fromEventNumber,
			int maxCount);
	}

	// Refers to a stream by name or by hash
	public struct StreamHandle {
		public static StreamHandle<TStreamId> ForHash<TStreamId>(ulong streamHash) {
			return new StreamHandle<TStreamId>(isHash: true, default, streamHash);
		}

		public static StreamHandle<TStreamId> ForStreamId<TStreamId>(TStreamId streamId) {
			return new StreamHandle<TStreamId>(isHash: false, streamId, default);
		}
	}

	// Refers to a stream by name or by hash
	// this unifies the entries, some just have the hash (when we know they do not collide)
	// some contain the full stream id (when they do collide)
	//qq consider explicit layout
	public readonly struct StreamHandle<TStreamId> {
		public readonly bool IsHash;
		public readonly TStreamId StreamId;
		public readonly ulong StreamHash;

		//qq sort out the order here so if the flag is true then it is using the second arg
		public StreamHandle(bool isHash, TStreamId streamId, ulong streamHash) {
			IsHash = isHash;
			StreamId = streamId;
			StreamHash = streamHash;
		}

		public override string ToString() =>
			IsHash
				? $"Hash: {StreamHash}"
				: $"Name: {StreamId}";
	}

	public interface ChunkTimeStampOptimisation {
		//qq we could have a dummy implemenation of this that just never kicks in
		// but could know, for each chunk, what the minimum timestamp of the records in
		// that chunk are within some range (to allow for incorrectly set clocks, dst etc)
		// then we could shortcut
		bool Foo(DateTime dateTime, long position);
	}

	//qq according to IndexReader.GetStreamLastEventNumberCached
	// if the original stream is hard deleted then the metadatastream is treated as deleted too
	// according to IndexReader.GetStreamMetadataCached
	// the metadata for a metadatastream cannot be overwritten
	//qq so if we get a metadata FOR a metadata stream, we should ignore it.
	// if we get a hard delete for a metadata stream
	//qq for all of thes consider how much precision (and therefore bits) we need
	//qq look at the places where we construct this, are we always setting what we need
	// might want to make an explicit constructor. can we easily find the places we are calling 'with' ?
	public record MetastreamData {
		public static readonly MetastreamData Empty = new(); //qq maybe dont need

		public MetastreamData() {
		}

		public ulong OriginalStreamHash { get; init; }
		public long? MaxCount { get; init; }
		public TimeSpan? MaxAge { get; init; } //qq can have limited precision?
		public long? TruncateBefore { get; init; }

		//qq consider if we want to have TB or jsut set the discard point directly
		// might need to store the TB separately if we need to recalculate the DP using
		// the TB later.
		public DiscardPoint? DiscardPoint { get; init; }

		//qq probably dont need this, but we could easily populate it if it is useful later.
		// its tempting because is would allow us to easily see which stream the metadata is for
		//public long MetadataPosition { get; init; } //qq to be able to scavenge the metadata

		//qq prolly at the others
		public override string ToString() => $"MaxCount: {MaxCount}";
	}

	//qq consider, if we were happy to not scavenge streams that collide, at least for now, we could
	// get away with only storing data for non-colliding keys in the magic map.

	//qq talk to Shaan about possible mono limitations

	//qq incidentally, could any of the scavenge state maps do with bloom filters for quickly skipping
	// streams that dont need to be scavenged

	//qqqq SUBSEQUENT SCAVENGES AND RELATED QUESTIONS
	// we dont run the next scavenge until the previous one has finished
	// which means we can be sure that the current discard points have been executed
	// (i.e. events before them removed) NO THIS IS NOT TRUE, the chunk might have been
	// below the threshold and not been scavenged. NO THIS IS TRUE as long as we require the
	// index to be scavenged.
	//
	// 1. what happens to the discard points, can we reuse them, do we need to recalculate them
	//    do we need to be careful when we recalculate them to take account of what was there before?
	// 2. in fact as this^ question of each part of the scavengestate.
	//
	//qq in the accumulator consider what should happen if the metadata becomes less restrictive
	// should we allow events that were previously excluded to re-appear.
	//  - we presumably dont want to allow cases where reads throw errors
	//  - if we want to be able to move the dp to make it less restrictive, then we need to
	//    store enough data to make sure that we dont undo the application of a different
	//    influence of DP. i.e. we cant apply the TB directly to the DP in the accumulator,
	//    otherwise we wouldn't know whether it was TB or Tombstone that set it, and therefore
	//    whether it can expand if we relax the TB.
	//
	//qqqq if we stored the previous and current discard point then can tell
	// from the index which events are new to scavenge this time - if that helps us
	// significantly with anything?

	//qq some events, like empty writes, do not contain data, presumably dont take up any numbering
	// see what old scavenge does with those and what we should do with them

	//qqqq TRANSACTIONS. these look ok
	// - We don't need to support Metadata or Tombstones committed in transactions.
	//    we will detect it and abort with an error. //qq but do detect it and test this
	// So we are only concerned with prepares in transactions.
	//
	// Accumulator: all we do with these is
	// - check for collisions (which we can do regardless of whether the record is in a transaction
	//   or not)
	// - accumulate timestamps - and these are the timestamps of the preparation not the commit
	//   so we can just look at the timestamp of the record regardless of what it is and
	//   whether it is in the transaction.
	// so the Accumulator is fine just reading the chunk without resorting to the allreader
	//
	// Calculator:
	// - This reads EventInfo from the index, which by its nature does not know about uncommitted events
	//   or commit records
	// - So it wont account for uncommited events or commit records in the 'count to be scavenged'
	// - But the DP should, i think, be calculated correctly.
	// so i think the calculator is fine too.
	//
	// ChunkExecutor:
	// - we can only scavenge a record if we know what event number it is, for which we need the commit
	//   record. so perhaps we only scavenge the events in a transaction (and the commit record)
	//   if the transaction was committed in the same chunk that it was started. this is pretty much
	//   what the old scavenge does too.
	//
	// IndexExecutor:
	// - i think this is fine by virtue of only having any knowledge of committed things
	//
	//  TRANSACTIONS IN OLD SCAVENGE
	//   - looks like uncommitted prepares are generally kept, maybe unless the stream is hard deleted
	//   - looks like even committed prepares are only removed if the commit is in the same chunk
	// - uncommitted transactions are not present in the index, neither are commit records
	
	//qqqq ponder out some notes on out-of-order and duplicate events

	//qq perhaps scavenge points go in a scavenge point stream so they can be easily found

	//qq RESUMING SCAVENGE
	// need some comments about this. consider what checkpoints we need to store in the scavenge state
	//  - accumulated up to
	//  - calculated up to
	//  - executed up to?

	//qq to consider in CollisionDetector any complications from previous index entries having been
	// scavenged.
	public record ScavengePoint {
		public long Position { get; set; }
		public DateTime EffectiveNow { get; set; }
	}
}
