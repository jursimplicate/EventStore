using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class CancellationAndContinuationTests {
		// in these tests we we want to
		// - run a scavenge
		// - have a log record trigger the cancellation of that scavenge at a particular point
		// - check that the checkpoint has been set correctly in the scavenge state
		// - complete the scavenge
		// - check it continued from the checkpoint
		// - check it produced the right results

		[Fact]
		public async Task accumulator_checkpoints_immediately() {
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
		public async Task calculator_checkpoints_immediately() {
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
			Assert.Equal(default, calculating.DoneStreamHandle);
		}

		[Fact]
		public async Task chunk_executor_checkpoints_immediately() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "cd-cancel-chunk-execution"))
					.CompleteLastChunk())
				.CancelWhenExecutingChunk("cd-cancel-chunk-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(checkpoint);
			Assert.Null(executing.DoneLogicalChunkNumber);
		}

		[Fact()]
		public async Task index_executor_checkpoints_immediately() {
			var (state, db) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "cd-cancel-index-execution"),
						Rec.Prepare(1, "ab-1"))
					.CompleteLastChunk())
				.CancelWhenExecutingIndexEntry("cd-cancel-index-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.ExecutingIndex>(checkpoint);
		}

		[Fact]
		public async Task can_cancel_during_accumulation_and_resume() {
			var (state, db) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount2),
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

			// now complete the scavenge
			(state, _) = await new Scenario()
				.WithDb(db)
				.WithState(x => x.ExistingState(state))
				// makes sure we dont reaccumulate the first chunk
				.CancelWhenAccumulatingMetaRecordFor("ab-1")
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0, 2),
					x.Recs[1].KeepIndexes(0),
					x.Recs[2].KeepIndexes(0),
				});

			// scavenge completed
			Assert.True(state.TryGetCheckpoint(out checkpoint));
			var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
			//qq Assert.Equal(thecorrectscavengepoint/number, done.?);
		}

		[Fact]
		public async Task can_cancel_during_calculation_and_resume() {
			var (state, db) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.Chunk(
						Rec.Prepare(3, "ab-1"),
						Rec.Prepare(4, "cd-cancel-calculation"),
						Rec.Prepare(5, "$$cd-cancel-calculation", metadata: MaxCount1))
					.CompleteLastChunk())
				.CancelWhenCalculatingOriginalStream("cd-cancel-calculation")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var calculating = Assert.IsType<ScavengeCheckpoint.Calculating<string>>(checkpoint);
			Assert.Equal("Hash: 98", calculating.DoneStreamHandle.ToString());

			// now complete the scavenge
			(state, _) = await new Scenario()
				.WithDb(db)
				.WithState(x => x.ExistingState(state))
				// make sure we dont reaccumulate
				.CancelWhenAccumulatingMetaRecordFor("ab-1")
				// make sure we dont recalculate
				.CancelWhenCalculatingOriginalStream("ab-1")
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0),
					x.Recs[1].KeepIndexes(0, 1, 2),
				});

			// scavenge completed
			Assert.True(state.TryGetCheckpoint(out checkpoint));
			var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
		}

		[Fact]
		public async Task can_cancel_during_chunk_execution_and_resume() {
			var (state, db) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.Chunk(
						Rec.Prepare(3, "$$ab-2", "$metadata", metadata: MaxCount1),
						Rec.Prepare(4, "cd-cancel-chunk-execution"),
						Rec.Prepare(5, "ab-2"))
					.CompleteLastChunk())
				.CancelWhenExecutingChunk("cd-cancel-chunk-execution")
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(checkpoint);
			Assert.Equal(0, executing.DoneLogicalChunkNumber);

			// now complete the scavenge
			(state, _) = await new Scenario()
				.WithDb(db)
				.WithState(x => x.ExistingState(state))
				// makes sure we dont reaccumulate
				.CancelWhenAccumulatingMetaRecordFor("ab-1")
				// make sure we dont recalculate
				.CancelWhenCalculatingOriginalStream("ab-1")
				// make sure we dont rescavenge the first chunk
				.CancelWhenExecutingChunk("ab-1")
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0, 1),
					x.Recs[1].KeepIndexes(0, 1, 2),
				});

			// scavenge completed
			Assert.True(state.TryGetCheckpoint(out checkpoint));
			var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
		}

		[Fact]
		public async Task can_cancel_during_index_execution_and_resume() {
			var (state, db) = await new Scenario()
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

			// now complete the scavenge
			(state, _) = await new Scenario()
				.WithDb(db)
				.WithState(x => x.ExistingState(state))
				// makes sure we dont reaccumulate
				.CancelWhenAccumulatingMetaRecordFor("ab-1")
				// make sure we dont recalculate
				.CancelWhenCalculatingOriginalStream("ab-1")
				// make sure we dont rescavenge the chunks
				.CancelWhenExecutingChunk("ab-1")
				//qq todo: consider making sure we dont rescavenge a ptable that was scavenged
				// but this is quite awkward, perhaps we can find another way to test it.
				// at least wait until we are not using the scaffold.
				//.CancelWhenExecutingIndexEntry("ab-1")

				// these are relative to the start of the continued scavenge
				// i.e. we dont expect the continued scavenge to remove any more from the chunks
				// and we expect the index to be brought into line.
				// which is as clear as mud but so far this is the only test that is affected
				.RunAsync(x => new[] {
					x.Recs[0],
					x.Recs[1],
				});

			// scavenge completed
			Assert.True(state.TryGetCheckpoint(out checkpoint));
			var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
		}

		//qq cancel and resume for any other stages we might add (merging, tidying)?

		[Fact]
		public async Task can_complete() {
			var (state, _) = await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.CompleteLastChunk())
				.RunAsync();

			Assert.True(state.TryGetCheckpoint(out var checkpoint));
			var executing = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
		}
	}
}
