using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class ScavengeCheckpointTests {
		private T RoundTrip<T>(T input, string expectedJson) where T : ScavengeCheckpoint {
			var json = ScavengeCheckpointJsonPersistence<string>.Serialize(input);
			Assert.True(ScavengeCheckpointJsonPersistence<string>.TryDeserialize(
				json,
				out var deserialized));
			Assert.Equal(expectedJson, json);
			return Assert.IsType<T>(deserialized);
		}

		[Theory]
		[InlineData(null, @"{""schemaVersion"":""V0"",""checkpointStage"":""Accumulating""}")]
		[InlineData(5, @"{""schemaVersion"":""V0"",""checkpointStage"":""Accumulating"",""doneLogicalChunkNumber"":5}")]
		public void can_round_trip_accumulating(int? x, string expectedJson) {
			var cp = RoundTrip(new ScavengeCheckpoint.Accumulating(x), expectedJson);
			Assert.Equal(x, cp.DoneLogicalChunkNumber);
		}

		[Fact]
		public void can_round_trip_calculating_default() {
			var cp = RoundTrip(
				new ScavengeCheckpoint.Calculating<string>(default),
				@"{""schemaVersion"":""V0"",""checkpointStage"":""Calculating""}");
			Assert.Equal(default, cp.DoneStreamHandle);
		}

		[Fact]
		public void can_round_trip_calculating_streamid() {
			var cp = RoundTrip(
				new ScavengeCheckpoint.Calculating<string>(StreamHandle.ForStreamId("stream1")),
				@"{""schemaVersion"":""V0"",""checkpointStage"":""Calculating""," + 
				@"""doneStreamHandle"":{""kind"":""Id"",""streamId"":""stream1""}}");
			Assert.Equal("Id: stream1", cp.DoneStreamHandle.ToString());
		}

		[Fact]
		public void can_round_trip_calculating_hash() {
			var cp = RoundTrip(
				new ScavengeCheckpoint.Calculating<string>(StreamHandle.ForHash<string>(97)),
				@"{""schemaVersion"":""V0"",""checkpointStage"":""Calculating""," +
				@"""doneStreamHandle"":{""kind"":""Hash"",""streamHash"":97}}");
			Assert.Equal("Hash: 97", cp.DoneStreamHandle.ToString());
		}

		[Theory]
		[InlineData(null, @"{""schemaVersion"":""V0"",""checkpointStage"":""ExecutingChunks""}")]
		[InlineData(5, @"{""schemaVersion"":""V0"",""checkpointStage"":""ExecutingChunks"",""doneLogicalChunkNumber"":5}")]
		public void can_round_trip_executing_chunks(int? x, string expectedJson) {
			var cp = RoundTrip(new ScavengeCheckpoint.ExecutingChunks(x), expectedJson);
			Assert.Equal(x, cp.DoneLogicalChunkNumber);
		}

		[Fact]
		public void can_round_trip_executing_index() {
			var cp = RoundTrip(
				new ScavengeCheckpoint.ExecutingIndex(),
				@"{""schemaVersion"":""V0"",""checkpointStage"":""ExecutingIndex""}");
			//qq Assert.Equal();
		}

		[Fact]
		public void can_round_trip_done() {
			var cp = RoundTrip(
				new ScavengeCheckpoint.Done(),
				@"{""schemaVersion"":""V0"",""checkpointStage"":""Done""}");
		}

		//qq and the others...
	}
}
