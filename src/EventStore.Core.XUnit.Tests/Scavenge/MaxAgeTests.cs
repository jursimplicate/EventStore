using System;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// for testing the maxage functionality specifically
	public class MaxAgeTests {
		[Fact]
		public async Task simple_maxage() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1", timestamp: Expired),
						Rec.Prepare(1, "ab-1", timestamp: Expired),
						Rec.Prepare(2, "ab-1", timestamp: Active),
						Rec.Prepare(3, "ab-1", timestamp: Active),
						Rec.Prepare(4, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
					.CompleteLastChunk())
				.RunAsync(
					x => new[] {
						x.Recs[0].KeepIndexes(2, 3, 4)
					},
					x => new[] {
						x.Recs[0]
					});
		}

		[Fact]
		public async Task keep_last_event() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1", timestamp: Expired),
						Rec.Prepare(1, "ab-1", timestamp: Expired),
						Rec.Prepare(2, "ab-1", timestamp: Expired),
						Rec.Prepare(3, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
					.CompleteLastChunk())
				.RunAsync(
					x => new[] {
						x.Recs[0].KeepIndexes(2, 3)
					},
					x => new[] {
						x.Recs[0]
					});
		}

		[Fact]
		public async Task whole_chunk_expired() {
			// the records can be removed from the chunks and the index
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1", timestamp: Expired),
						Rec.Prepare(1, "ab-1", timestamp: Expired),
						Rec.Prepare(2, "ab-1", timestamp: Expired))
					.Chunk(
						Rec.Prepare(3, "ab-1", timestamp: Active),
						Rec.Prepare(4, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
					.CompleteLastChunk())
				.RunAsync(
					x => new[] {
						x.Recs[0].KeepIndexes(),
						x.Recs[1].KeepIndexes(0, 1),
					});
		}

		[Fact]
		public async Task whole_chunk_expired_keep_last_event() {
			// the records can be removed from the chunks and the index
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1", timestamp: Expired),
						Rec.Prepare(1, "ab-1", timestamp: Expired),
						Rec.Prepare(2, "ab-1", timestamp: Expired))
					.Chunk(
						Rec.Prepare(3, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
					.CompleteLastChunk())
				.RunAsync(
					x => new[] {
						x.Recs[0].KeepIndexes(2),
						x.Recs[1].KeepIndexes(0),
					});
		}

		[Fact]
		public async Task whole_chunk_active() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1", timestamp: Active),
						Rec.Prepare(1, "ab-1", timestamp: Active),
						Rec.Prepare(2, "ab-1", timestamp: Active))
					.Chunk(
						Rec.Prepare(3, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
					.CompleteLastChunk())
				.RunAsync(
					x => new[] {
						x.Recs[0],
						x.Recs[1],
					});
		}
	}
}
