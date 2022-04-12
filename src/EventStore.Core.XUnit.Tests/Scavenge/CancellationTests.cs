using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class CancellationTests : ScavengerTestsBase {
		[Fact]
		public async Task cancel_during_accumulation() {
			//qq so, we want to
			// - run a scavenge
			// - have a particular log record trigger the cancellation of that scavenge
			// - check that the checkpoint has been set correctly in the scavenge state
			// - complete the scavenge, check the results.

			//qq the accumulator can cancel by injecting a collision storage scavenge map

			var state = await CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "ab-1"),
					Rec.Prepare(1, "ab-1"))
				.Chunk(
					Rec.Prepare(2, "£cancel-accumulation"))
				.CompleteLastChunk())
				.CancelWhenHashing("£cancel-accumulation")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var accumulating = Assert.IsType<ScavengeCheckpoint.Accumulating>(checkpoint);
			Assert.Equal(1, accumulating.DoneLogicalChunkNumber);
		}
	}
}
