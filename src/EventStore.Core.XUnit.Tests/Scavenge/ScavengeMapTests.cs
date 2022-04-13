using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class ScavengeMapTests {
		//qq todo: prolly add more tests, run against the different implementations

		[Fact]
		public void from_checkpoint() {
			var sut = new InMemoryScavengeMap<int, string>();
			sut[0] = "0";
			sut[1] = "1";
			sut[2] = "2";
			sut[3] = "3";
			sut[4] = "4";

			Assert.Collection(
				sut.FromCheckpoint(2),
				x => Assert.Equal("3", x.Value),
				x => Assert.Equal("4", x.Value));
		}
	}
}
