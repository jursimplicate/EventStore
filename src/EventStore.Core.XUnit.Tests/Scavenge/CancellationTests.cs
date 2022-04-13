using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class CancellationTests {
		//qqqqqqqqqqqqqqqqqqqq in these tests we we want to
		// [*] run a scavenge
		// [*] have a particular log record trigger the cancellation of that scavenge
		// [*] check that the checkpoint has been set correctly in the scavenge state
		// [ ] complete the scavenge, check the results.
		//qq add tests for cancellation as soon as each component has started

		[Fact]
		public async Task can_cancel_during_accumulation_immediate() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$cd-cancel-accumulation"))
					.CompleteLastChunk())
				.CancelWhenAccumulatingMetaRecordFor("cd-cancel-accumulation")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var accumulating = Assert.IsType<ScavengeCheckpoint.Accumulating>(checkpoint);
			Assert.Null(accumulating.DoneLogicalChunkNumber);
		}

		[Fact]
		public async Task can_cancel_during_accumulation() {
			var (state, db) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.Chunk(
						Rec.Prepare(3, "$$cd-cancel-accumulation"))
					.Chunk(
						Rec.Prepare(4, "ab-1"))
					.CompleteLastChunk())
				.CancelWhenAccumulatingMetaRecordFor("cd-cancel-accumulation")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var accumulating = Assert.IsType<ScavengeCheckpoint.Accumulating>(checkpoint);
			Assert.Equal(0, accumulating.DoneLogicalChunkNumber);

			// now want to complete the scavenge
			//qq which means we want to start from the database and index and scavenge state that
			// we got up to last time, and then run scavenge on it.
			// we want to create a new scenario with that state, and with different cancelation injection

			//qqqqqqqq fill in
			(state, db) = await new Scenario()
				.WithDb(db)
				.WithState(x => x.ExistingState(state))
				.RunAsync();
		}

		[Fact]
		public async Task can_cancel_during_calculation_immediate() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "cd-cancel-calculation"),
						Rec.Prepare(1, "$$cd-cancel-calculation", metadata: MaxCount1))
					.CompleteLastChunk())
				.CancelWhenCalculatingOriginalStream("cd-cancel-calculation")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var calculating = Assert.IsType<ScavengeCheckpoint.Calculating<string>>(checkpoint);
			Assert.Null(calculating.DoneStreamHandle);
		}

		[Fact]
		public async Task can_cancel_during_calculation() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.Chunk(
						Rec.Prepare(3, "cd-cancel-calculation"),
						Rec.Prepare(4, "$$cd-cancel-calculation", metadata: MaxCount1))
					.CompleteLastChunk())
				.CancelWhenCalculatingOriginalStream("cd-cancel-calculation")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var calculating = Assert.IsType<ScavengeCheckpoint.Calculating<string>>(checkpoint);
			Assert.Equal("Hash: 98", calculating.DoneStreamHandle.ToString());
		}

		[Fact]
		public async Task can_cancel_during_chunk_execution_immediate() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"),
						Rec.Prepare(4, "cd-cancel-chunk-execution"))
					.CompleteLastChunk())
				.CancelWhenExecutingChunk("cd-cancel-chunk-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(checkpoint);
			Assert.Null(executing.DoneLogicalChunkNumber);
		}

		[Fact]
		public async Task can_cancel_during_chunk_execution() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.Chunk(
						Rec.Prepare(3, "ab-1"),
						Rec.Prepare(4, "cd-cancel-chunk-execution"),
						Rec.Prepare(5, "ab-1"))
					.CompleteLastChunk())
				.CancelWhenExecutingChunk("cd-cancel-chunk-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(checkpoint);
			Assert.Equal(0, executing.DoneLogicalChunkNumber);
		}

		[Fact]
		public async Task can_cancel_during_index_execution() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.Chunk(
						Rec.Prepare(3, "ab-1"),
						Rec.Prepare(4, "cd-cancel-index-execution"),
						Rec.Prepare(5, "ab-1"))
					.CompleteLastChunk())
				.CancelWhenExecutingIndexEntry("cd-cancel-index-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.ExecutingIndex>(checkpoint);
		}

		[Fact]
		public async Task can_complete() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.CompleteLastChunk())
				.CancelWhenExecutingIndexEntry("cd-cancel-index-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
		}
	}
}
