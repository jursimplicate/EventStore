using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq this may end up being a wrapper around the magic map and not a memory specific implementation
	// at all.
	//qq instructions are not necessarily the right name for this now.
	//public class InMemoryScavengeInstructions<TStreamId> : IScavengeInstructions<TStreamId> {
	//	public InMemoryScavengeInstructions() {
	//	}

	//	public IEnumerable<IReadOnlyChunkScavengeInstructions<TStreamId>> ChunkInstructionss => throw new System.NotImplementedException();

	//	public bool TryGetDiscardPoint(TStreamId streamId, out DiscardPoint discardPoint) {
	//		throw new System.NotImplementedException();
	//	}
	//}

	public class InMemoryChunkScavengeInstructions<TStreamId> : IChunkScavengeInstructions<TStreamId> {
		public int ChunkNumber => throw new System.NotImplementedException();

		public IDictionary<TStreamId, long> EarliestEventsToKeep {
			get => throw new System.NotImplementedException();
			set => throw new System.NotImplementedException();
		}

		public int NumRecordsToDiscard { get; private set; }

		public void Discard() {
			throw new System.NotImplementedException();
		}
	}

	public class InMemoryIndexReaderForAccumulator<TStreamId> : IIndexReaderForAccumulator<TStreamId> {
		public bool HashInUseBefore(ulong hash, long postion, out TStreamId candidedateCollidee) {
			//qq
			throw new System.NotImplementedException();
		}
	}
}
