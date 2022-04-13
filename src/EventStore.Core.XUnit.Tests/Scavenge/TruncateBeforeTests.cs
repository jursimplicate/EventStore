using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// for testing the truncatebefore functionality specifically
	public class TruncateBeforeTests {
		[Fact]
		public async Task simple_truncatebefore() {
			await new Scenario().WithDb(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "ab-1"),
					Rec.Prepare(2, "ab-1"),
					Rec.Prepare(3, "ab-1"),
					Rec.Prepare(4, "$$ab-1", "$metadata", metadata: TruncateBefore3))
				.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(3, 4)
				});
		}

		[Fact]
		public async Task keep_last_event() {
			await new Scenario().WithDb(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "ab-1"),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: TruncateBefore4))
				.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1,2)
				});
		}
	}
}
