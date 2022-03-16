using System;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq for now lets keep this really simple and just use index reader.
	//add support for the scavenge point limiting later.
	public class IndexForScavenge<TStreamId> : IIndexReaderForCalculator<TStreamId> {
		private readonly IReadIndex<TStreamId> _readIndex;

		public IndexForScavenge(IReadIndex<TStreamId> readIndex) {
			_readIndex = readIndex;
		}

		//qq todo respect scavengepoint
		public long GetLastEventNumber(StreamHandle<TStreamId> stream, long scavengePoint) {
			if (stream.IsHash) {
				throw new NotImplementedException();
				//qq index only!
				// return _readIndex.GetStreamLastEventNumber(stream.StreamHash);
			} else {
				// uses log to check for hash collisions
				return _readIndex.GetStreamLastEventNumber(stream.StreamId);
			}
		}

		//qq naiiive. pobably want to chop, or if we dis something like tracked the min
		// timestamp per chunk then we could narrow it down to a particular chunk really cheaply.
		// that would possibly be easy enough to keep in the scavenge state
		//public long GetLastEventNumber(TStreamId streamId, DateTime age) {
		//	var done = false;
		//	while (!done) {
		//		_readIndex.ReadStreamEventsBackward(
		//			"",
		//			streamId,
		//			long.MaxValue,
		//			100);
		//	}
		//	return 0;
		//}

		public EventInfo[] ReadEventInfoForward(
			StreamHandle<TStreamId> stream,
			long fromEventNumber,
			int maxCount) {

			if (stream.IsHash) {
				//qq index only!
				throw new NotImplementedException();
			} else {
				// uses log to check for hash collisions
				var result = _readIndex.ReadStreamEventsForward(
					"", //qq
					stream.StreamId,
					fromEventNumber,
					maxCount);

				//qq do we need to look at the other things like .Result, .IsEndOfStream etc
				return null; //qq	result.Records;
			}
		}
	}

	//public class IndexForScavenge<TStreamId> : IIndexForScavenge<TStreamId> {
	//	private readonly ITableIndex<TStreamId> _tableIndex;

	//	public IndexForScavenge(ITableIndex<TStreamId> tableIndex) {
	//		_tableIndex = tableIndex;
	//	}

	//	public long GetLastEventNumber(TStreamId streamId, long scavengePoint) {
	//		//qqq we want to avoid repeated lookups, so we could possibly pass around some datastructure
	//		// that lazily keeps what we want to keep for a particular stream.

	//		//qq this wants to get the last event number for a given stream but before a scavengepoint.
	//		//qq could we reasonably use this structure that we are going to accumulate to be the 
	//		// initial cut of the new index?
	//		// remember we dont allow backups during a scavenge

	//		// we probably want to start by getting the 

	//		//qqqqq this is surprisingly awkward.
	//		// - but maybe it will get much less awkward with the new indexes, and much quicker
	//		          (no hash collisions)
	//		//   in which case maybe we should just delegate this to the index for now so that
	//		//   we dont have to change the scavenge logic later.
	//		// - we could generalize the code in index reader to take a 'up to' log point
	//		// - we could write the logic that accesses the tableindex here
	//		// - we could store the last event number in the streamdata...
	//		//     if we can think of a nice way to do it.
	//		//        faster -> would produce quite a lot of garbage in the log
	//		//        sqlite?? -> maybe slow and iointensive
	//		//        either way perhaps it will get complicated with backups

	//		//qqq this is a bit naive or inefficient maybe, perhaps there are corner cases, but it'll get us going.
	//		var indexEntries = _tableIndex.GetRange(
	//			streamId: streamId,
	//			startVersion: 0,
	//			endVersion: long.MaxValue,
	//			limit: 100); //qq

	//	}
	//}
}
