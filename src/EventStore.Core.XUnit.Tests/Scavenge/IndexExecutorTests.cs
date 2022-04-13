using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// these systemtically exercise the cases in the IndexExecutor
	// we still do so by testing high level scavenge cases because we are well geared up
	// for that and testing the IndexExeecutor directly would involve more mocks than it is worth.
	public class IndexExecutorTests {
		[Fact]
		public async Task nothing_to_scavenge() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0, 1, 2)
				});
		}

		[Fact]
		public async Task simple_scavenge() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"),
						Rec.Prepare(3, "$$ab-1", "$metadata", metadata: MaxCount2))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1, 2, 3)
				});
		}

		[Fact]
		public async Task with_collision() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Prepare(1, "cb-2"),
						Rec.Prepare(2, "ab-1"),
						Rec.Prepare(3, "ab-1"),
						Rec.Prepare(4, "$$ab-1", "$metadata", metadata: MaxCount2))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1, 2, 3, 4)
				});
		}
	}
}
