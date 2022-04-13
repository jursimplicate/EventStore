using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Index;
using EventStore.Core.TransactionLog.Chunks;

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
		Task RunAsync(ITFChunkScavengerLog scavengerLogger, CancellationToken cancellationToken);
		//qq options
		// - timespan, or datetime to autostop
		// - chunk to scavenge up to
		// - effective 'now'
		// - remove open transactions : bool
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
		void Accumulate(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.Accumulating checkpoint,
			IScavengeStateForAccumulator<TStreamId> state,
			CancellationToken cancellationToken);
	}

	// The Calculator calculates the DiscardPoints that depend on the ScavengePoint
	// (after which all the scavengable streams will have discard points calculated correctly)
	//
	// It also creates a heuristic for which chunks are most in need of scavenging.
	//  - an approximate count of the number of records to discard in each chunk
	//     //qq explain why it is approximate. 
	//         - we don't count commit records (but, i think, if we are able to scavenge a commit record
	//            then we will have increased the weight of the chunk for the sake of the event records	//             
	//         - we don't currently count metadata records, but we probably could if its worth it
	//             each time we find a metadata record we need to increment the count for the chunk
	//             of the _old_ metadata record, which we can find because we stored its position yay
	//         - any other reason?
	//
	// The job of calculating the DiscardPoints is split between the Accumulator and the Calculator.
	// Some things affect the DiscardPoints in a fairly static way and can be applied to the
	// DiscardPoint directly in the Accumulator. Others set criteria for the DiscardPoint that cause
	// the DiscardPoint to move regularly. For these latter one we delay applying their effect to the
	// DiscardPoint until the calculator, to save us updating them over and over.
	//
	//  - Tombstone : Accumulator
	//  - Static Metadata MaxCount 1 : Accumulator
	//  - Metadata TruncateBefore : Calculator
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
	// For streams that do not collide (which is ~all of them) the calculation can be done index-only.
	// that is, without hitting the log at all.
	public interface ICalculator<TStreamId> {
		void Calculate(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.Calculating<TStreamId> checkpoint,
			IScavengeStateForCalculator<TStreamId> source,
			CancellationToken cancellationToken);
	}

	// the chunk executor performs the actual removal of the log records
	// should be very rare to do any further lookups at this point.
	public interface IChunkExecutor<TStreamId> {
		void Execute(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.ExecutingChunks checkpoint,
			IScavengeStateForChunkExecutor<TStreamId> state,
			CancellationToken cancellationToken);
	}

	// the index executor performs the actual removal of the index entries
	// should be very rare to do any further lookups at this point.
	public interface IIndexExecutor<TStreamId> {
		void Execute(
			ScavengePoint scavengePoint,
			ScavengeCheckpoint.ExecutingIndex checkpoint,
			IScavengeStateForIndexExecutor<TStreamId> state,
			IIndexScavengerLog scavengerLogger,
			CancellationToken cancellationToken);
	}













	//
	// FOR ACCUMULATOR
	//

	//qq note dont use allreader to implement this, it has logic to deal with transactions, skips
	// epochs etc.
	public interface IChunkReaderForAccumulator<TStreamId> {
		IEnumerable<RecordForAccumulator<TStreamId>> ReadChunk(int logicalChunkNumber);
	}

	//qq could use streamdata? its a class though
	public abstract class RecordForAccumulator<TStreamId> {
		public TStreamId StreamId { get; set; }
		public long LogPosition { get; set; }
		public DateTime TimeStamp { get; set; }

		//qq make sure to recycle these.
		//qq prolly have readonly interfaces to implement, perhaps a method to return them for reuse
		// Record in Original Stream
		public class OriginalStreamRecord : RecordForAccumulator<TStreamId> {
			public OriginalStreamRecord() {}
		}

		// Record in metadata stream
		public class MetadataStreamRecord : RecordForAccumulator<TStreamId> {
			public MetadataStreamRecord() {}
			public StreamMetadata Metadata { get; set; }
			public long EventNumber { get; set; }
		}

		// tombstones are identified by their prepare flags
		// if thats even possible
		public class TombStoneRecord : RecordForAccumulator<TStreamId> {
			public TombStoneRecord() {}
			// old scavenge, index writer and index committer are set up to handle
			// tombstones that have abitrary event numbers, so lets handle them here
			// in case it used to be possible to create them.
			public long EventNumber { get; set; }
		}
	}











	//
	// FOR CALCULATOR
	//

	//qq name
	public interface IIndexReaderForCalculator<TStreamId> {
		//qq maxposition  / positionlimit instead of scavengepoint?
		long GetLastEventNumber(StreamHandle<TStreamId> streamHandle, ScavengePoint scavengePoint);

		//qq maybe we can do better than allocating an array for the return
		EventInfo[] ReadEventInfoForward(
			StreamHandle<TStreamId> stream,
			long fromEventNumber,
			int maxCount,
			ScavengePoint scavengePoint);
	}

	public readonly struct EventInfo {
		public readonly long LogPosition;
		public readonly long EventNumber;

		public EventInfo(long logPosition, long eventNumber) {
			LogPosition = logPosition;
			EventNumber = eventNumber;
		}
	}










	//
	// FOR CHUNK EXECUTOR
	//

	public interface IChunkManagerForChunkExecutor<TStreamId, TChunk> {
		IChunkWriterForExecutor<TStreamId, TChunk> CreateChunkWriter(
			int chunkStartNumber,
			int chunkEndNumber);

		IChunkReaderForExecutor<TStreamId> GetChunkReaderFor(long position);

		bool TrySwitchChunk(
			TChunk chunk,
			bool verifyHash,
			bool removeChunksWithGreaterNumbers,
			out string newFileName);
	}

	public interface IChunkReaderForExecutor<TStreamId> {
		int ChunkStartNumber { get; }
		int ChunkEndNumber { get; }
		bool IsReadOnly { get; }
		long ChunkEndPosition { get; }
		IEnumerable<int> LogicalChunkNumbers { get; }
		//qq this is probably just the prepares, rename accordingly?
		IEnumerable<RecordForScavenge<TStreamId>> ReadRecords();
	}

	public interface IChunkWriterForExecutor<TStreamId, TChunk> {
		TChunk WrittenChunk { get; }

		void WriteRecord(RecordForScavenge<TStreamId> record);
		//qq finalize, etc.
	}







	//
	// FOR INDEX EXECUTOR
	//

	public interface IIndexScavenger {
		void ScavengeIndex(
			long scavengePoint,
			Func<IndexEntry, bool> shouldKeep,
			IIndexScavengerLog log,
			CancellationToken cancellationToken);
	}

	public interface IChunkReaderForIndexExecutor<TStreamId> {
		bool TryGetStreamId(long position, out TStreamId streamId);
	}
















	//
	// MISC
	//

	// So that the scavenger knows where to scavenge up to
	public interface IScavengePointSource {
		ScavengePoint GetScavengePoint();
	}

	// when scavenging we dont need all the data for a record
	//qq but we do need more data than this
	// but the bytes can just be bytes, in the end we are going to keep it or discard it.
	//qq recycle this record like the recordforaccumulation?
	//qq hopefully doesn't have to be a class, or can be pooled
	public class RecordForScavenge<TStreamId> {
		public RecordForScavenge() {
		}

		public TStreamId StreamId { get; set; }
		public DateTime TimeStamp { get; set; }
		public long EventNumber { get; set; }
		//qq pool
		public byte[] RecordBytes { get; set; }
	}

	//qqq this is now IStateForChunkExecutor
	//public interface IScavengeInstructions<TStreamId> {
	//	//qqqqq is chunknumber the logical chunk number?
	//	//qq do we want to store a separate file per logical chunk or per physical (merged) chunk.
	//	IEnumerable<IReadOnlyChunkScavengeInstructions<TStreamId>> ChunkInstructionss { get; }
	//	//qq this isn't quite it, prolly need stream name
	//	bool TryGetDiscardPoint(TStreamId streamId, out DiscardPoint discardPoint);
	//}

	// Refers to a stream by name or by hash
	// This struct is json serialized, don't change the names naively
	public struct StreamHandle {
		//qq consider specifying byte if we are going to end up with a lot of these in memory
		public enum Kind {
			None,
			Hash,
			Id,
		};

		public static StreamHandle<TStreamId> ForHash<TStreamId>(ulong streamHash) {
			return new StreamHandle<TStreamId>(kind: Kind.Hash, default, streamHash);
		}

		public static StreamHandle<TStreamId> ForStreamId<TStreamId>(TStreamId streamId) {
			return new StreamHandle<TStreamId>(kind: Kind.Id, streamId, default);
		}
	}

	// Refers to a stream by name or by hash
	// this unifies the entries, some just have the hash (when we know they do not collide)
	// some contain the full stream id (when they do collide)
	//qq consider explicit layout
	public readonly struct StreamHandle<TStreamId> {

		public readonly StreamHandle.Kind Kind;
		public readonly TStreamId StreamId;
		public readonly ulong StreamHash;

		public StreamHandle(StreamHandle.Kind kind, TStreamId streamId, ulong streamHash) {
			Kind = kind;
			StreamId = streamId;
			StreamHash = streamHash;
		}

		public override string ToString() {
			switch (Kind) {
				case StreamHandle.Kind.Hash:
					return $"Hash: {StreamHash}";
				case StreamHandle.Kind.Id:
					return $"Id: {StreamId}";
				case StreamHandle.Kind.None:
				default:
					return $"None";
			};
		}
	}

	//qq according to IndexReader.GetStreamLastEventNumberCached
	// if the original stream is hard deleted then the metadatastream is treated as deleted too
	// according to IndexReader.GetStreamMetadataCached
	// the metadata for a metadatastream cannot be overwritten
	//qq so if we get a metadata FOR a metadata stream, we should ignore it.
	//qq if we get a tombstone for a metadata stream?
	//     - see how the system handles it for reads. if it ignores it we should too. if it clears the metadata we should too
	//qq for all of thes consider how much precision (and therefore bits) we need
	//qq look at the places where we construct this, are we always setting what we need
	// might want to make an explicit constructor. can we easily find the places we are calling 'with' ?
	//qq for everything in here consider signed/unsigned and the number of bits and whether it needs to
	// but nullable vs, say, using -1 to mean no value.
	//qq consider whether to make this immutable or reusable and make sure we are using it appropriately.
	public class OriginalStreamData {
		public static OriginalStreamData Empty { get; } = new OriginalStreamData(); //qq maybe dont need

		public OriginalStreamData() {
		}

		// Populated by Accumulator. Read by Calculator.
		// (MaxAge also read by ChunkExecutor)
		public long? MaxCount { get; set; }
		public TimeSpan? MaxAge { get; set; } //qq can have limited precision?
		public long? TruncateBefore { get; set; }
		public bool IsTombstoned { get; set; }

		// Populated by Calculator. Read by Calculator and Executors.
		public DiscardPoint DiscardPoint { get; set; }
		public DiscardPoint MaybeDiscardPoint { get; set; }

		public override string ToString() =>
			$"MaxCount: {MaxCount} " +
			$"MaxAge: {MaxAge} " +
			$"TruncateBefore: {TruncateBefore} " +
			$"IsTombstoned: {IsTombstoned} " +
			$"DiscardPoint: {DiscardPoint} " +
			$"MaybeDiscardPoint: {MaybeDiscardPoint} " +
			"";
	}

	// For ChunkExecutor, which implements maxAge more accurately than the index executor
	public struct StreamExecutionDetails {
		public StreamExecutionDetails(
			DiscardPoint discardPoint,
			DiscardPoint maybeDiscardPoint,
			TimeSpan? maxAge) {

			DiscardPoint = discardPoint;
			MaybeDiscardPoint = maybeDiscardPoint;
			MaxAge = maxAge;
		}

		public DiscardPoint DiscardPoint { get; }
		public DiscardPoint MaybeDiscardPoint { get; }
		public TimeSpan? MaxAge { get; }
	}

	//qq implement performance overrides as necessary for this struct and others
	// (DiscardPoint, StreamHandle, ..)
	// store a range per chunk so that the calculator can definitely get a timestamp range for each event
	// that is guaranteed to to contain the real timestamp of that event.
	// if we inferred the min from the previous chunk, its possible than an incorrect clock could
	// give us an empty range for a chunk, which would be awkward to deal with in the calculator.
	public struct ChunkTimeStampRange {
		//qq reduce the precision to save persistent space.
		// say nearest minute is good enough, round min down to nearest minute and max up.
		// this is only used to control the index scavenge and to give a heuristic to the 
		// chunk scavenge so, 1 minute is probably more than plenty. look at how much resolution
		// we can get for different numbers of bytes.
		public ChunkTimeStampRange(DateTime min, DateTime max) {
			Min = min;
			Max = max;
		}

		public DateTime Min { get; }

		public DateTime Max { get; }
	}

	//qq some kind of configurable speed throttle on each of the phases to stop them hogging iops

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
	//
	//qq after a scavenge there are probaly some entries in the scavenge state that we can forget about
	// to save us having to iterate them next time. for example if it was tombtoned. and perhaps tb
	// as long as the tb was fully executed.


	//qq some events, like empty writes, do not contain data, presumably dont take up any numbering
	// see what old scavenge does with those and what we should do with them

	//qqqq TRANSACTIONS. these look ok
	// - is it possible that an uncommitted transaction could have a hash collision in
	// a way that we wouldn't detect but would be important? nah, because there is no such thing as a
	// collision except in the index. but we should have a tests that makes sure its ok when we have
	// uncommitted transactions that "collide"
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


	//qq perhaps scavenge points go in a scavenge point stream so they can be easily found

	//qq make it so that the scavenge state can be deleted and run again
	// if we delete the scavenge state and run a scavenge, do we want it to
	// process each scavenge point or just the last? presumably just the last because there could
	// be a lot of them.

	//qq consider compatibility with old scavenge
	// - config flag to choose which scavenge to run
	// - old by default
	// you definitely have to be able to run old scavenge first, thats typical.
	// will it work to run old scavenge after new scavenge? don't see why not
	// will it work to interleave them?
	// if we want to disable the old scavenge after running the new we could
	//    - bump the chunk schema version
	//    - have old scavenge check for scavengepoints and abort?

	//qq RESUMING SCAVENGE
	// need some comments about this. consider what checkpoints we need to store in the scavenge state
	//  - accumulated up to
	//  - calculated up to
	//  - executed up to?
	// and also recovery from crashing during a flush. idempotency

	//qqqq need comment/plan about EXPANDING metadatas.
	//   probably we want to follow the same behaviour as reads so that the visible data doesn't
	//   change when you run a scavenge.
	//qqqq need comment/plan about that flag that allows the last event to be removed.
	//qqqq need comment/plan on out of order and duplicate events

	//qq can you tombstone a metadata stream
	//qq if you tombstone a stream does it probably has no effect on the metadtaa stream since we
	// already scavenge all but the last event, and we want to keep the last event.

	//qq to consider in CollisionDetector any complications from previous index entries having been
	// scavenged.

	//qq note, probably need to complain if the ptable is a 32bit table

	//qq backup strategy: same as current, only take a backup while scavenge is not running. this is
	// point towards not having the accumulator running continuall too

	//qq dont forget about chunk merging.. maybe is that another phase after execution, or part of
	// execution.

	//qq DETERMINISTIC SCAVENGE
	// there are some interesting things to note/decide about this
	// - when we write the scavenge point, it is in the open chunk, which we can't scavenge.
	//   so at the time we write it, we cant scavenge up to the scavenge point.
	//   but if we scavenged later, we would be able to scavenge right up to it.
	//   but we want the scavenge to be deterministic, so perhaps it should always only scavenge
	//   up to the end of the chunk before the chunk containing the scavenge point.
	//   BUT what does it really mean to not scavenge the open chunk
	//     - can we accumulate the content of the open chunk?
	//     - can we calculate according to the content of the open chunk?
	//     - can we execute the index of things that are in the open chunk?
	//     - can we execute the open chunk itself?
	//    quite possibly the answer to all these except the last is yes and still keep determinism.
	//
	// - to be deterministic, we may want to the chunk weight threshold (and other things?
	//    unsafediscardtombstones?) properties of the ScavengePoint and instead of configuration options?
	//
	// - implications if the scavenge state is deleted, could we jump to the last scavenge point, or
	//   would we need to scavenge each in turn (hopefully the former)
	//
	// - new scavenge is deterministic but the current state is important, ponder these:
	//    - if old scavenge left the chunks in different states, new scavenge won't necessarily bring
	//      them in line, or will it.
	//    - if two nodes are at different points of the scavenge, then the chunks could be in different
	//      states
	//
	// - if we delete the scavenge state and want to rebuild it (say it is corrupted in a backup, say
	//   they took the backup while scavenge was running)
	//   then it would be sad if we had to rerun every scavenge point
	//   but if we can somehow jump to the last one, then perhaps a node that just hasnt been scavenged
	//   in several points could just jump to the last one also.
	//   in any of these cases getting the chunk version numbering right could be awkward, i wonder
	//   whether it is important.
	//   even if we have to jump through some hoops for the chunks, we pretty surely only need to
	//   scavenge the index once.
	//
	// - if we put the threshold in the scavenge points, perhaps to skip over scavenge points you just
	//   need to min/max the threshold.
	//
	// - if we could get a better idea of what goes wrong when you mix and match chunks today, then we
	//   could make sure that we could avoid those problems without being overly strict (like perhaps
	//   the chunk version numbering isnt important, perhaps the exact way that the chunks are merged
	//   isnt important. etc)



	//qq the hash collision detection is pretty key. it is how we efficiently scavenge the index.
	// if we don't spot what collisions there are, and simply calculate DiscardPoints and store them
	// against stream names, then (aside from the additional disk space, iops to read and write, seach
	// time to traverse that structure) we wouldn't be able to tell what the discard point is for any
	// given indexentry without looking up the log record to see what stream it is for. repeat for
	// _every_ indexentry.
	//
	// todo:
	// - start creating high level tests to test the scavenger (see how the old scavenge tests work, we
	//   may be able to use them - the effect of the scavenge ought to be pretty much identical to the
	//   user but the effect on the log will be different so may not be able to use exactly the same
	//   tests (e.g. we will drop in scavenge points, might not scavenge the same things exactly that
	//   are done on a best effort basis like commit records)
	// - once we have an idea or two from v20, port the code over to v5.
	// - port the ScavengeState tests over to the higher level tests
	// - pass through the doc in case there are more things to think about
	// - diagram out the components so we can get the big picture
	// - want the same unit tests to run against mock implementations and real implementations of
	//      the adapter interfaces ideally
	// - implement/test the rest of the logic in the scavenge (tdd)
	//     - tidying phase
	//     - stopping/resuming
	//     - ...
	// - implement/test the adapters that plug it in to the rest of the system
	//     - includes extra apis to the index but they ought to not be too bad
	// - implement/test the persistent scavengemap
	// - integrate starting/stopping with eventstore proper
	// - performance testing
	// - forward port to master - ptables probably wont be a trivial change
	// - probably forward/back port to 20.10 and 21.10
	// - write docs
	// - 
	// ...
	//
	// dimensions for testing (perhaps in combination also)
	//   - committed transactions
	//   - uncommitted transactions
	//   - transactions crossing chunks
	//   - setting maxage/maxcount/tb
	//   - expanding metadata
	//   - contracting metadata
	//   - metadata for streams that dont exist
	//   - metadata for streams that do exist but are created after
	//   - tombstones
	//   - tombstone in transaction
	//   - metadata in transaction
	//   - tombstone in metadata stream
	//   - metadata for metadata stream
	//   - stopped/resumed scavenge
	//   - initial/subsequent scavenge
	//   - merged chunks
	//   - log record schema versions (esp wrt tombstones since it affects what the max value is)
	//   - ...
	//
	//
	//qq we can split the scavenge state by
	// - metadata vs original stream
	//    - downside, index executor doesn't know which map to look in. double lookup.
	// - metastreamdata vs discard point
	//    - i.e. store allll the discard points in one map
	// - all in one map
	//    - downside: wasted space, or complexity of variable length values
	//
	//qq REDACTION
	//  needs notes

	//qq TOMBSTONES
	// tldr: tombstones do not necessarily have eventnumber int.maxvalue or long.maxvalue.
	//       but they do have PrepareFlags.StreamDelete.
	//       we are therefore reluctant to just have the accumulator capture tombstones the DiscardPoint
	//       because it won't be obvious that the discard point represents a tombstone. some cases later
	//       _might_ involve moving the DiscardPoint backwards but we must not do that if it represents
	//       a tombstone. For now keep it simple and obvious and store the istombstone explicitly
	//       and _not_ in the discard point. however dont keep it for metastreams, the discard point
	//       set by the accumulator accurately describes the required effect, and there are no
	//       complications of having other metadata affect it for metadata streams.
	// 
	//
	// Although the StorageWriterService does, these days, create tombstones with
	// EventNumber = EventNumber.DeletedStream the TFChunkScavenger, IndexComitter, and IndexWriter are
	// all geared up not to rely on that, but to rely on the prepareflags instead. It looks like they use
	// this to causes index.GetStreamLastEventNumber to return long.max (i.e. EventNumber.DeletedStream)
	// regardless of the eventnumber of the tombstone. SO lets assume that some old tombstones have other
	// eventnumbers and use the prepareflag to detect it.
	//
	// Note, if there is such a tombstone whose eventnumber is not long.max, then that means that its
	// event number in its indexentry would be different to its event number in the log.
	//
	// So, have done this:
	//  - go back to having a 'is tombstoned' flag
	//  - get rid of the 'tombstone' discard point. i.e. we keep them separate again.
	//  then
	//  - we can probably persist it in the sign bit of the discard point itself
	//  - then we have an explicit representation of tombstoning seprate to the discard point, which
	//    helps to simplify things
	//  - saves us from needing to spot tombtones (via their prepare flags) in the chunk executor,
	//    just obey the DP.
	//
	//qq tempting to 'optimise' this to a smaller size by storing the the 'IsTombstoned' and maybe
	// the TruncateBefore _in_ the DiscardPoint, but it would make the semantics less clear, the code
	// less obvious and probably less flexible. dont optimise this yet.

	//qq OLD INDEX SCAVENGE TESTS
	// - there is no need to run these tests against new scavenge.
	// - in a nutshell they test the TableIndex (and PTables), which new and old scavenge both use.
	// - they just test that the tableindex scavenge works correctly given a function that determines
	//   whether to keep each index entry. in the tests this function is injected via the fakeReader
	//   and it (for example) trivially checks if the position is in a list of deleted positions.
	//   therefore these tests are assuming a correct shouldKeep method and checking that TableIndex
	//   responds appropriately (when index upgrades, cancellation, awaiting tables etc). there would be
	//   no advantage to running them again injecting our own dummy shouldkeep implementation.
	//
	//qq make sure whenever we are cashing to prepare and accessing eventnumber that we are coping with
	// it being part of a transaction
	//qq make sure we never interact with index entries beyong the scavengepoint because we don't know
	// whether they are collisions or not
	
	//qq add a test that covers a chunk becoming empty on scavenge

	public class ScavengePoint {
		//qq do we want these to be explicit, or implied from the position/timestamp
		// of the scavenge point itself? questions is whether there is any need to scavenge
		// at a different time or place.
		//qq consider that at the time we place the scavenge point it is not in a scavengable chunk
		//   we should probably capture that behaviour here and have Position (rename to
		//   EffectivePosition?) return the point that we can really scavenge up to, whatever that is.

		public long Position { get; set; }
		public DateTime EffectiveNow { get; set; }
		//qqqqq public long ScavengePointNumber { get; set; }
	}
}
