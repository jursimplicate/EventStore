using System;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// for testing functionality that isn't specific to particular discard criteria
	public class MiscelaneousTests {
		[Fact]
		public async Task metadata_first() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"),
						Rec.Prepare(3, "ab-1"),
						Rec.Prepare(4, "ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(0, 4)
					});
		}

		[Fact]
		public async Task nonexistent_stream() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(0)
					});
		}

		[Fact]
		public async Task multiple_streams() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Prepare(1, "cd-2"),
						Rec.Prepare(2, "ab-1"),
						Rec.Prepare(3, "cd-2"),
						Rec.Prepare(4, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(5, "$$cd-2", "$metadata", metadata: MaxCount1))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(2, 3, 4, 5)
					});
		}

		[Fact]
		public async Task metadata_gets_scavenged() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "$$ab-1", "$metadata", metadata: MaxCount2))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(1)
					});
		}

		[Fact]
		public async Task metadata_for_metadata_stream_gets_scavenged() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$$$ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "$$$$ab-1", "$metadata", metadata: MaxCount2))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(1)
					});
		}

		[Fact]
		public async Task metadata_for_metadata_stream_does_not_apply() {
			// e.g. can't increase the maxcount to three
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$$$ab-1", "$metadata", metadata: MaxCount3),
						Rec.Prepare(1, "$$ab-1"),
						Rec.Prepare(2, "$$ab-1"),
						Rec.Prepare(3, "$$ab-1"),
						Rec.Prepare(4, "$$ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(0, 4)
					});
		}

		[Fact]
		public async Task metadata_metadata_applies_to_any_type() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "$$ab-1"),
						Rec.Prepare(1, "$$ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0].KeepIndexes(1)
					});
		}

		[Fact]
		public async Task metadata_in_normal_stream_is_ignored() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1", "$metadata", metadata: MaxCount1),
						Rec.Prepare(1, "ab-1"),
						Rec.Prepare(2, "ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
						x.Recs[0]
					});
		}

		[Fact]
		public async Task metadata_in_transaction_not_supported() {
			var e = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await new Scenario()
					.WithDb(x => x
						.Chunk(
							Rec.TransSt(0, "$$ab-1"),
							Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1))
						.CompleteLastChunk())
					.RunAsync();
			});

			Assert.Equal("Found metadata in transaction in stream $$ab-1", e.Message);
		}
	}
}
