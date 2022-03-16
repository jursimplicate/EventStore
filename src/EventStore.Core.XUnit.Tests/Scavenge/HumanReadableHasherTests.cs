using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class HumanReadableHasherTests {
		[Fact]
		public void hashes_original_stream() {
			var sut = new HumanReadableHasher();
			Assert.Equal((ulong)'a'.GetHashCode(), sut.Hash("ma-1"));
		}

		[Fact]
		public void hashes_meta_stream() {
			var sut = new HumanReadableHasher();
			Assert.Equal((ulong)'m'.GetHashCode(), sut.Hash("$$ma-1"));
		}

		[Fact]
		public void hashes_empty_string() {
			var sut = new HumanReadableHasher();
			Assert.Equal(0UL, sut.Hash(""));
		}
	}
}
