using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class CombinationCriteriaTests {
		//qq need more of these, check that the criteria work well in different combinations
		// and set in different orders
		[Fact]
		public async Task maxcount_then_tombstone() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount2),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"),
						Rec.Delete(3, "ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0, 3)
				});
		}

		[Fact]
		public async Task tombstone_then_maxcount() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Prepare(1, "ab-1"),
						Rec.Delete(2, "ab-1"),
						Rec.Prepare(3, "$$ab-1", "$metadata", metadata: MaxCount2))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}
	}
}
