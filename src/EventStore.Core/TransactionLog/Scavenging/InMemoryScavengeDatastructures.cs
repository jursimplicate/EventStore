using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging {
	//qq this may end up being a wrapper around the state and not a memory specific implementation
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

	public class InMemoryIndexReaderForAccumulator<TStreamId> : IIndexReaderForAccumulator<TStreamId> {
		public bool HashInUseBefore(ulong hash, long postion, out TStreamId hashUser) {
			throw new System.NotImplementedException();
		}
	}
}
