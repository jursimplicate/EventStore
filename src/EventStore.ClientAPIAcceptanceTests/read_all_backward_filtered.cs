using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.TransactionLogV2.Services;
using Xunit;

namespace EventStore.ClientAPI.Tests {
	public class read_all_backward_filtered : EventStoreClientAPITest, IAsyncLifetime {
		private readonly EventStoreClientAPIFixture _fixture;

		public read_all_backward_filtered(EventStoreClientAPIFixture fixture) {
			_fixture = fixture;
		}

		[Theory, ClassData(typeof(StreamIdFilterCases))]
		public async Task stream_id_filter_returns_expected_result(bool useSsl, Func<string, Filter> getFilter,
			string name) {
			var streamPrefix = $"{GetStreamName()}_{useSsl}_{name}";
			var testEvents = _fixture.CreateTestEvents(10).ToArray();

			var connection = _fixture.Connections[useSsl];

			foreach (var e in testEvents) {
				await connection.AppendToStreamAsync($"{streamPrefix}_{Guid.NewGuid():n}", ExpectedVersion.NoStream, e);
			}

			var result = await connection.FilteredReadAllEventsBackwardAsync(
				Position.End, 4096, false, getFilter(streamPrefix)).WithTimeout();

			//Assert.Equal(ReadDirection.Backward, result.ReadDirection);
			Assert.Equal(testEvents.Select(x => x.EventId), result.Events
				.Reverse()
				.Select(x => x.OriginalEvent.EventId));
		}

		[Theory, ClassData(typeof(EventTypeFilterCases))]
		public async Task event_type_filter_returns_expected_result(EventTypeFilterCases.Case @case) {
			var eventTypePrefix = $"{GetStreamName()}_{@case.UseSsl}_{@case.FilterType}";

			var testEvents = _fixture.CreateTestEvents(10)
				.Select(e =>
					new EventData(e.EventId, $"{eventTypePrefix}-{Guid.NewGuid():n}", e.IsJson, e.Data, e.Metadata))
				.ToArray();

			var connection = _fixture.Connections[@case.UseSsl];

			foreach (var e in testEvents) {
				await connection.AppendToStreamAsync(Guid.NewGuid().ToString("n"), ExpectedVersion.NoStream, e);
			}

			var result = await connection.FilteredReadAllEventsBackwardAsync(
				Position.End, 4096, false, @case.CreateFilter(eventTypePrefix)).WithTimeout();

			//Assert.Equal(ReadDirection.Backward, result.ReadDirection);
			Assert.Equal(testEvents.Select(x => x.EventId), result.Events
				.Reverse()
				.Select(x => x.OriginalEvent.EventId));
		}

		public async Task InitializeAsync() {
			var connection = _fixture.Connections[false];;

			await connection.SetStreamMetadataAsync("$all", ExpectedVersion.Any,
				StreamMetadata.Build().SetReadRole(SystemRoles.All), DefaultUserCredentials.Admin).WithTimeout();
		}

		public async Task DisposeAsync() {
			var connection = _fixture.Connections[false];;

			await connection.SetStreamMetadataAsync("$all", ExpectedVersion.Any,
				StreamMetadata.Build().SetReadRole(null), DefaultUserCredentials.Admin).WithTimeout();
		}
	}
}
