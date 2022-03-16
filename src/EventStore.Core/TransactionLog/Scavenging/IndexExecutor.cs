using EventStore.Core.Index;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class IndexExecutor<TStreamId> : IIndexExecutor<TStreamId> {
		public IndexExecutor(IDoStuffForIndexExecutor stuff) {

		}

		public void Execute(IScavengeStateForIndexExecutor<TStreamId> instructions) {
			//qq fill this in, scavenge the ptables
			var ptables = new[] { 2, 3 }; //qq temp
			foreach (var ptable in ptables) {
				ExecutePTable(instructions, ptable);
			}
		}

		public void ExecutePTable(
			IScavengeStateForIndexExecutor<TStreamId> state,
			int ptable) {

			//qq get the index entries from the ptable
			var indexEntries = new[] {
				new IndexEntry(stream: 123, version: 456, position: 789),
			};

			var currentHash = (ulong?)null;
			var currentHashIsCollision = false;
			var discardPoint = DiscardPoint.KeepAll;

			foreach (var indexEntry in indexEntries) {
				//qq to decide whether to keep an index entry we need to 
				// 1. determine which stream it is for
				// 2. look up the discard point for that stream
				// 3. see if it is to be discarded.
				if (currentHash != indexEntry.Stream || currentHashIsCollision) {
					// on to a new stream, get its discard point.
					currentHash = indexEntry.Stream;
					currentHashIsCollision = state.IsCollision(indexEntry.Stream);

					discardPoint = GetDiscardPoint(
						state,
						currentHash.Value,
						currentHashIsCollision);
				}

				if (discardPoint.ShouldDiscard(indexEntry.Version)) {
					// drop index entry
				} else {
					//qq keep index entry, copy it to output
				}
			}

			//qq save ptable, swap it in, etc.
		}

		private DiscardPoint GetDiscardPoint(
			IScavengeStateForIndexExecutor<TStreamId> state,
			ulong hash,
			bool isCollision) {

			StreamHandle<TStreamId> handle = default;

			if (isCollision) {
				// collision, we need to get the event for this position, see what stream it is
				// really for, and look up the discard point by streamId.
				TStreamId streamId = default; //qq _index.ReadEvent(indexEntry.Position).StreamId;
				handle = StreamHandle.ForStreamId(streamId);

			} else {
				// not a collision, we can get the discard point by hash.
				handle = StreamHandle.ForHash<TStreamId>(hash);
			}

			return state.TryGetDiscardPoint(handle, out var discardPoint)
				? discardPoint
				: DiscardPoint.KeepAll;
		}
	}
}
