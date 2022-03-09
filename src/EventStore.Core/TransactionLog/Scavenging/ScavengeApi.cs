using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq consider the name
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

	// the accumulator reads through the log up to the scavenge point
	// its purpose is to do any log scanning that is necessary for a scavenge _only once_
	// accumulating whatever state is necessary to avoid subsequent scans.
	//
	// in practice it
	//  1. finds the scavengable streams, which are
	//     1. streams with metadata (user and system)
	//     2. metadata streams
	//  2. spots hash collisions
	//  3. notes down metadata as it finds it. 
	public interface IAccumulator<TStreamId> {
		void Accumulate(ScavengePoint scavengePoint, IScavengeStateForAccumulator<TStreamId> state);
		//qq got separate apis for adding and getting state cause they'll probably be done
		// by different logical processes
		IScavengeStateForCalculator<TStreamId> ScavengeState { get; }
	}

	//qqqq consider api. consider name
	// the purpose of the calculator is to calculate what needs to be scavenged for a given scavenge point
	// i.e. it calculates the discardpoint for each scavengeable stream.
	// we don't calculate this during the accumulation phase because the decisionpoints would keep
	// moving as we go (e.g. time is passing for maxage, new events would move the dp on maxcount streams)
	// so to avoid doing duplicate work we dont do that until calculation phase.
	// it also means the accumulator only needs to actually process a small amount of data in the log.
	// note that this is a bit different to noting down the metdata as we go, since this only changes when
	// someone actually writes a new metadta record, which is presumably not tooo often.
	// the structure this produces is enough to quickly scavenge the chunks and ptables without
	// (typically) doing any further lookups or calculation.
	public interface ICalculator<TStreamId> {
		// processed so far.
		void Calculate(ScavengePoint scavengePoint, IScavengeStateForCalculator<TStreamId> source);
	}

	// the executor does the actual removal of the log records and index records
	// should be very rare to do any further lookups at this point.
	public interface IChunkExecutor<TStreamId> {
		void Execute(IScavengeStateForChunkExecutor<TStreamId> instructions);
	}

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

		public class TombStoneRecord : RecordForAccumulator<TStreamId> {
			public TStreamId StreamId { get; set; }
			public long LogPosition { get; set; }
		}

		//qq make sure we only instantiate these for metadata records in metadata streams
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
		//public long MetadataPosition { get; init; } //qq to be able to scavenge the metadata

		//qq prolly at the others
		public override string ToString() => $"MaxCount: {MaxCount}";
	}

	//qq consider, if we were happy to not scavenge streams that collide, at least for now, we could
	// get away with only storing data for non-colliding keys in the magic map.



	public record ScavengePoint {
		public long Position { get; set; }
		public DateTime EffectiveNow { get; set; }
	}
}
