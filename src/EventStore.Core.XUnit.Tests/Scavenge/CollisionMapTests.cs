using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// generally the properties we need of the CollisionManager are tested at a higher
	// level. but a couple of fiddly bits are checked in here
	public class CollisionMapTests {
		//qq rename
		[Fact]
		public void foo_attempt_at_originalstreamdatas() {
			var collisions = new InMemoryScavengeMap<string, Unit>();
			var sut = GenSut(collisions);

			// add a non-collision for ab-1
			sut["ab-1"] = "foo";

			// value can be retrieved
			Assert.True(sut.TryGetValue("ab-1", out var actual));
			Assert.Equal("foo", actual);

			// "a-1" collides with "a-2".
			collisions["ab-1"] = Unit.Instance;
			collisions["ab-2"] = Unit.Instance;
			sut.NotifyCollision("ab-1");

			// value for "a-1" can still be retrieved
			Assert.True(sut.TryGetValue("ab-1", out actual));
			Assert.Equal("foo", actual);

			// "a-2" not retrieveable since we never set it
			Assert.False(sut.TryGetValue("ab-2", out _));
		}

		[Fact]
		public void can_enumerate() {
			var collisions = new InMemoryScavengeMap<string, Unit>();
			var sut = GenSut(collisions);

			collisions["ac-1"] = Unit.Instance;
			collisions["ac-2"] = Unit.Instance;
			collisions["ac-3"] = Unit.Instance;

			// non collisions
			sut["ad-4"] = "4";
			sut["ae-5"] = "5";
			sut["af-6"] = "6";
			// collisions
			sut["ac-1"] = "1";
			sut["ac-2"] = "2";
			sut["ac-3"] = "3";

			// no checkpoint
			Assert.Collection(
				sut.Enumerate(checkpoint: default),
				x => Assert.Equal("(Id: ac-1, 1)", $"{x}"),
				x => Assert.Equal("(Id: ac-2, 2)", $"{x}"),
				x => Assert.Equal("(Id: ac-3, 3)", $"{x}"),
				x => Assert.Equal("(Hash: 100, 4)", $"{x}"),
				x => Assert.Equal("(Hash: 101, 5)", $"{x}"),
				x => Assert.Equal("(Hash: 102, 6)", $"{x}"));

			// id checkpoint
			Assert.Collection(
				sut.Enumerate(checkpoint: StreamHandle.ForStreamId("ac-2")),
				x => Assert.Equal("(Id: ac-3, 3)", $"{x}"),
				x => Assert.Equal("(Hash: 100, 4)", $"{x}"),
				x => Assert.Equal("(Hash: 101, 5)", $"{x}"),
				x => Assert.Equal("(Hash: 102, 6)", $"{x}"));

			// hash checkpoint
			Assert.Collection(
				sut.Enumerate(checkpoint: StreamHandle.ForHash<string>(101)),
				x => Assert.Equal("(Hash: 102, 6)", $"{x}"));

			// end checkpoint
			Assert.Empty(sut.Enumerate(checkpoint: StreamHandle.ForHash<string>(102)));
		}

		private static CollisionMap<string, string> GenSut(IScavengeMap<string, Unit> collisions) {
			var sut = new CollisionMap<string, string>(
				new HumanReadableHasher(),
				x => collisions.TryGetValue(x, out _),
				new InMemoryScavengeMap<ulong, string>(),
				new InMemoryScavengeMap<string, string>());

			return sut;
		}
	}
}
