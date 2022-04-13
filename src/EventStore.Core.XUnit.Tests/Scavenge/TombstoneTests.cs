using System;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class TombstoneTests {
		//qqq we will probably want a set of these to test out collisions when there are tombstones
		// and a set for collisions when there is metadata (maybe combination metadata and tombstone)
		[Fact]
		public async Task simple_tombstone() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Prepare(0, "ab-1"),
						Rec.Delete(1, "ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1)
				});
		}

		[Fact]
		public async Task single_tombstone() {
			await new Scenario()
				.WithDb(x => x
					.Chunk(
						Rec.Delete(1, "ab-1"))
					.CompleteLastChunk())
				.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0)
				});
		}

		[Fact]
		public async Task tombstone_in_metadata_stream_not_supported() {
			// eventstore refuses denies access to write such a tombstone in the first place,
			// including in ESv5
			var e = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await new Scenario()
					.WithDb(x => x
						.Chunk(
							Rec.Delete(0, "$$ab-1"))
						.CompleteLastChunk())
					.RunAsync();
			});

			Assert.Equal("Found Tombstone in metadata stream $$ab-1", e.Message);
		}
	}
}
