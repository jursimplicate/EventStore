namespace EventStore.Core.TransactionLog.Scavenging {
	public class ChunkExecutor<TStreamId> : IChunkExecutor<TStreamId> {
		private readonly IChunkManagerForChunkExecutor _chunkManager;
		private readonly IChunkReaderForChunkExecutor<TStreamId> _chunkReader;
		public ChunkExecutor(
			IChunkManagerForChunkExecutor chunkManager,
			IChunkReaderForChunkExecutor<TStreamId> chunkReader) {

			_chunkManager = chunkManager;
			_chunkReader = chunkReader;
		}

		public void Execute(
			ScavengePoint scavengePoint,
			IScavengeStateForChunkExecutor<TStreamId> scavengeState) {

			//qq would we want to run in parallel? (be careful with scavenge state interactions
			// in that case, especially writes)
			//qq order by the weight?
			//qq do we need to do them all
			//qq we probably wanna iterate through all the chunks though, not just the ones that we
			// stored a weight for
			//qq limited by a scavenge point

			foreach (var chunkWeight in scavengeState.GetChunkWeights(scavengePoint)) {
				ExecuteChunk(scavengeState, chunkWeight);
				//qq careful if parallel
				scavengeState.OnChunkScavenged(chunkWeight.ChunkNumber);
			}
		}

		private void ExecuteChunk(
			IScavengeStateForChunkExecutor<TStreamId> scavengeState,
			ChunkWeight chunkWeight) {

			//qq might this want to consider the size of the chunk too, since it may have been scavenged
			// before
			var threshold = 1000;
			if (chunkWeight.Weight < threshold) {
				// they'll still (typically) be removed from the index
				return;
			}

			//qq the other reason we might want to not scanvenge this chunk is if the posmap would make
			// it bigger
			// than the original... limited concern because of the threshold above BUT we could address
			// by either
			//   - improving the posmap 
			//   - using a padding/scavengedevent system event to prevent having to write a posmap
			// this is the kind of decision we can make in here, local to the chunk.
			// knowing the numrecordstodiscard could be useful here, if we are just discarding a small
			// number then we'd probably pad them with gone events instead of adding a posmap.

			// in ExecuteChunk could also be a reasonable place to do a best effort at removing commit
			// records if all the prepares for the commit are in this chunk (typically the case) and they
			// are all scavenged, then we can remove the commit as well i think. this is probably what
			// the old scavenge does. check

			// old scavenge says 'never delete the very first prepare in a transaction'
			// hopefully we can account for that here? although maybe it means our count of
			// records to scavenge that was calculated index only might end up being approximate.

			// 1. open the chunk, probably with the bulk reader

			//qq do this with a strategy so we can plug bulk reader in.
			
			//qq is the one in the chunkinstructions a logical chunk number or physical?
			// if physical, then we can get the physical chunk from the chunk manager and process it
			// if logical then bear in mind that the chunk we get from the chunk manager is the whole
			// physical file
			var chunk = _chunkManager.GetChunk(chunkWeight.ChunkNumber);

			//qq var newChunk = ???;

			foreach (var record in _chunkReader.Read(chunk)) {
				//qq here we don't care about whether there were hash collisions or not
				// delegate that to the 'instructions'

				//qq the discard point is pesimistic with maxage. if we want (configurable?),
				// since we have the record here, we could look up the maxage and discard the event based
				// on that. note we shouldn't do this in the index since there we don't already have the
				// record and it would be more expensive to look it up. but the index is fine with us
				// removing events from the log without removing them from the index

				//qq hmm events in transactions do not have an EventNumber
				if (!scavengeState.TryGetDiscardPoint(record.StreamId, out var discardPoint))
					discardPoint = DiscardPoint.KeepAll;

				if (discardPoint.ShouldDiscard(record.EventNumber)) {

					//qq discard record
				} else {
					//qq keep record
					//qq do we need to upgrade it?
					//qq will using the bulk reader be awkward considering the record format
					// size changes that have occurred over the years
					// if so consider using the regular reader.
					// what does the old scavenge use
					// consider transactions
				}
			}
			// 2. read through it, keeping and discarding as necessary. probably no additional lookups at
			// this point
			// 3. write the posmap
			// 4. finalise the chunk
			// 5. swap it in to the chunkmanager
			//qq _chunkManager.SwitchChunk();
		}
	}
}
