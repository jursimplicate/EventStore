using System;

namespace EventStore.Core.TransactionLog.Scavenging {
	// null checkpoint means no checkpoint
	// Accumulating with null done means we are accumulating now but havent accumulated anything.

	public abstract class ScavengeCheckpoint {
		//qqqq need the scavengepoint number in here so we know which scavenge point we are
		// working on. this node can be several scavenge points behind, and other nodes can
		// even add new scavenge points while we are working on a different one
		protected ScavengeCheckpoint() {
		}

		public class Accumulating : ScavengeCheckpoint {
			public Accumulating(int? doneLogicalChunkNumber) {
				DoneLogicalChunkNumber = doneLogicalChunkNumber;
			}

			public int? DoneLogicalChunkNumber { get; }
		}

		public class Calculating<TStreamId> : ScavengeCheckpoint {
			public Calculating(StreamHandle<TStreamId> doneStreamHandle) {
				DoneStreamHandle = doneStreamHandle;
			}

			public StreamHandle<TStreamId> DoneStreamHandle { get; }
		}

		public class ExecutingChunks : ScavengeCheckpoint {
			public int? DoneLogicalChunkNumber { get; }

			public ExecutingChunks(int? doneLogicalChunkNumber) {
				DoneLogicalChunkNumber = doneLogicalChunkNumber;
			}
		}

		public class ExecutingIndex : ScavengeCheckpoint {
			public ExecutingIndex() {
			}
		}

		public class Merging : ScavengeCheckpoint {
			public Merging() {
			}
		}

		//qq name. if this is a phase at all.
		public class Tidying : ScavengeCheckpoint {
			public Tidying() {
			}
		}

		public class Done : ScavengeCheckpoint {
		}
	}
}
