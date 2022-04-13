using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// for testing the maxcount functionality specifically
	public class MaxCountTests {
		[Fact]
		public async Task simple_maxcount() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"),
						Rec.Prepare(3, "ab-1"),
						Rec.Prepare(4, "$$ab-1", "$metadata", metadata: MaxCount1))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(3, 4)
				});
		}
	}
}
