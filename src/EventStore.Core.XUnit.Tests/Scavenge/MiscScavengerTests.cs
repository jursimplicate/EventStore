﻿using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	//qq split into suitable test classes
	public class MiscScavengerTests : ScavengerTestsBase {
		//qq there is Rec.TransSt and TransEnd.. what do prepares and commits mean here without those?
		// probably applies to every test in here
		[Fact]
		public void Trivial() {
			CreateScenario(x => x
				.Chunk(
					// the first letter of the stream name determines its hash value
					// a-1:       a stream called "a-1" which hashes to "a"
					Rec.Prepare(0, "ab-1"),

					// setting metadata for a-1, which does not collide with a-1
					//qq um this isn't in a metadata stream so it probably wont be recognised as metadata
					// instead the stream should be called "ma1" which hashes to #a "$$ma1" which hashes
					// to #m and the hasher chooses which character depending on whether it is a metadta
					// stream.
					Rec.Prepare(1, "$$ab-1", "$metadata", metadata: MaxCount1))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(0, 1)
				});
		}

		[Fact]
		public void seen_stream_before() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "ab-1"))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(0, 1)
				});
		}

		[Fact]
		public void collision() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "ab-2"))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(0, 1)
				});
		}

		[Fact]
		public void metadata_non_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "$$ab-1", "$metadata", metadata: MaxCount1))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(0, 1)
				});
		}

		//qq now that we are keying on the metadta streams, does that mean that we don't
		// need to many cases here? like whether or not the original streams collide might not be
		// relevant any more.
		//
		//qqqqqqqqqqqqq do we want to bake tombstones into here as well
		[Fact]
		public void metadata_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "aa-1"),
					Rec.Prepare(1, "$$aa-1", "$metadata", metadata: MaxCount1))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(0, 1)
				});
		}

		[Fact]
		public void metadata_applies_to_correct_stream_in_hidden_collision() {
			// metastream sets metadata for stream ab-1 (which hashes to b)
			// but that stream doesn't exist.
			// make sure that cb-2 (which also hashes to b) does not pick up that metadata.
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "cb-2"),
					Rec.Prepare(2, "cb-2"))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(0, 1, 2)
				});
		}

		[Fact]
		public void metadatas_for_different_streams_non_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$cd-2", "$metadata", metadata: MaxCount2),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Prepare(3, "$$cd-2", "$metadata", metadata: MaxCount4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}

		[Fact]
		public void metadatas_for_different_streams_all_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$aa-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$aa-2", "$metadata", metadata: MaxCount2),
					Rec.Prepare(2, "$$aa-1", "$metadata", metadata: MaxCount3),
					Rec.Prepare(3, "$$aa-2", "$metadata", metadata: MaxCount4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}

		[Fact]
		public void metadatas_for_different_streams_original_streams_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$cb-2", "$metadata", metadata: MaxCount2),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Prepare(3, "$$cb-2", "$metadata", metadata: MaxCount4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}

		[Fact]
		public void metadatas_for_different_streams_meta_streams_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$ac-2", "$metadata", metadata: MaxCount2),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Prepare(3, "$$ac-2", "$metadata", metadata: MaxCount4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}

		[Fact]
		public void metadatas_for_different_streams_original_and_meta_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$ab-2", "$metadata", metadata: MaxCount2),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Prepare(3, "$$ab-2", "$metadata", metadata: MaxCount4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}

		[Fact]
		public void metadatas_for_different_streams_cross_colliding() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$ba-2", "$metadata", metadata: MaxCount2),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Prepare(3, "$$ba-2", "$metadata", metadata: MaxCount4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(2, 3)
				});
		}

		[Fact]
		public void metadatas_for_same_stream() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Prepare(1, "$$ab-1", "$metadata", metadata: MaxCount2))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(1)
				});
		}
	}
}