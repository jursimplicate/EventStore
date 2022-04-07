﻿using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// for testing the truncatebefore functionality specifically
	public class TruncateBeforeTests : ScavengerTestsBase {
		[Fact]
		public void simple_truncatebefore() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "ab-1"),
					Rec.Prepare(2, "ab-1"),
					Rec.Prepare(3, "ab-1"),
					Rec.Prepare(4, "$$ab-1", "$metadata", metadata: TruncateBefore3))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(3, 4)
				});
		}

		[Fact]
		public void keep_last_event() {
			CreateScenario(x => x
				.Chunk(
					Rec.Prepare(0, "ab-1"),
					Rec.Prepare(1, "ab-1"),
					Rec.Prepare(2, "$$ab-1", "$metadata", metadata: TruncateBefore4))
				.CompleteLastChunk())
				.Run(x => new[] {
					x.Recs[0].KeepIndexes(1,2)
				});
		}
	}
}
