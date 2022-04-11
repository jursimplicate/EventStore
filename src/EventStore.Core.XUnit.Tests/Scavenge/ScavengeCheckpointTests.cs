using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class ScavengeCheckpointTests {
		[Fact]
		public void json_is_sensible() {
			var accumulating = new ScavengeCheckpoint.Accumulating(5);
			var json = ScavengeCheckpointJsonPersistence<string>.Serialize(accumulating);

			Assert.Equal(
				@"{""schemaVersion"":""V0"",""checkpointStage"":""Accumulating"",""doneLogicalChunkNumber"":5}",
				json);
		}

		private T RoundTrip<T>(T input) where T : ScavengeCheckpoint {
			var json = ScavengeCheckpointJsonPersistence<string>.Serialize(input);
			Assert.True(ScavengeCheckpointJsonPersistence<string>.TryDeserialize(
				json,
				out var deserialized));
			return Assert.IsType<T>(deserialized);
		}

		[Fact]
		public void can_round_trip_accumulating() {
			var cp = RoundTrip(new ScavengeCheckpoint.Accumulating(5));
			Assert.Equal(5, cp.DoneLogicalChunkNumber);
		}

		[Fact]
		public void can_round_trip_calculating() {
			var cp = RoundTrip(new ScavengeCheckpoint.Calculating<string>("stream1"));
			Assert.Equal("stream1", cp.DoneStreamId);
		}

		[Fact]
		public void can_round_trip_executing_chunks() {
			var cp = RoundTrip(new ScavengeCheckpoint.ExecutingChunks(5));
			Assert.Equal(5, cp.DoneLogicalChunkNumber);
		}

		[Fact]
		public void can_round_trip_executing_index() {
			var cp = RoundTrip(new ScavengeCheckpoint.ExecutingIndex());
			//qq Assert.Equal();
		}

		//qq and the others...
	}
}
