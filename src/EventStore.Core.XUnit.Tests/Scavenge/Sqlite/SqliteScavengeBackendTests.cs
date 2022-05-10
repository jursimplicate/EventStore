using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite {
	public class SqliteScavengeBackendTests : DirectoryPerTest<SqliteScavengeBackendTests> {
		
		[Fact]
		public void should_successfully_enable_features_on_initialization() {
			var sut = new SqliteScavengeBackend<string>();

			var result = Record.Exception(() => sut.Initialize(Fixture.Directory));
			Assert.Null(result);
		}
	}
}
