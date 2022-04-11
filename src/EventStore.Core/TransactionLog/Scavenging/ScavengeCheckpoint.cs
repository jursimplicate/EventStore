using System;

namespace EventStore.Core.TransactionLog.Scavenging {

	public abstract class ScavengeCheckpoint {
		protected ScavengeCheckpoint() {
		}

		public class Accumulating : ScavengeCheckpoint {
			public Accumulating(int doneLogicalChunkNumber) {
				DoneLogicalChunkNumber = doneLogicalChunkNumber;
			}

			public int DoneLogicalChunkNumber { get; }
		}

		public class Calculating<TStreamId> : ScavengeCheckpoint {
			public Calculating(TStreamId doneStreamId) {
				DoneStreamId = doneStreamId;
			}

			public TStreamId DoneStreamId { get; }
		}

		public class ExecutingChunks : ScavengeCheckpoint {
			public int DoneLogicalChunkNumber { get; }

			public ExecutingChunks(int doneLogicalChunkNumber) {
				DoneLogicalChunkNumber = doneLogicalChunkNumber;
			}
		}

		public class ExecutingIndex : ScavengeCheckpoint {
			public ExecutingIndex() {

			}
			// public List<Guid> DonePTables { get; } //qq could be interesting
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
	}
}
