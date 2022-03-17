//using System;
//using System.Collections.Generic;
//using EventStore.Core.Index.Hashes;
//using EventStore.Core.TransactionLog.Scavenging;
//using Xunit;

//namespace EventStore.Core.XUnit.Tests.Scavenge {
//	//qq probably want to have a persistent collision resolver which is largely tested in the same way
//	public class InMemoryCollisionResolverTests {
//		private readonly ILongHasher<string> _hasher = new FirstCharacterHasher();

//		InMemoryCollisionResolver<string, string> GenSut() {
//			var collisionDetector = new CollisionDetector<string>(HashInUseBefore);
//			static bool HashInUseBefore(string stream, long recordPosition, out string hashUser) {
//				throw new NotImplementedException();
//			}

//			var sut = new InMemoryCollisionResolver<string, string>(_hasher, collisionDetector);
//			return sut;
//		}

//		[Fact]
//		public void CanRetrieveByKey() {
//			var sut = GenSut();

//			sut["a-1"] = "value1";

//			Assert.True(sut.TryGetValue("a-1", out var valueA));
//			Assert.Equal("value1", valueA);
//			Assert.Equal("value1", sut["a-1"]);
//		}

//		[Fact]
//		public void CanRetrieveByKeyColliding() {
//			throw new NotImplementedException();
//		}

//		[Fact]
//		public void CanAttemptToFindMissingKeys() {
//			var sut = GenSut();

//			Assert.False(sut.TryGetValue("a", out _));
//			Assert.Throws<KeyNotFoundException>(() => {
//				var v = sut["a"];
//			});
//		}

//		[Fact]
//		public void CanEnumerate() {
//			var sut = GenSut();

//			sut["a-1"] = "value1";
//			sut["b-2"] = "value2";

//			Assert.Collection(
//				sut.Enumerate(),
//				x => Assert.Equal((StreamHandle.ForHash<string>(_hasher.Hash("a")), "value1"), x),
//				x => Assert.Equal((StreamHandle.ForHash<string>(_hasher.Hash("b")), "value2"), x));
//		}

//		[Fact]
//		public void CanEnumerateWithCollisions() {
//			var sut = GenSut();

//			sut["a-1"] = "value1";
//			sut["b-2"] = "value2";
//			sut["a-3"] = "value3"; // <-- collides with a-1

//			// now when we enumerate, because there are collisions, we need to be able to see the keys
//			// so that we can resolve the collisions when we read from the index.
//			Assert.Collection(
//				sut.Enumerate(),
//				x => Assert.Equal((StreamHandle.ForStreamId("a-1"), "value1"), x),
//				x => Assert.Equal((StreamHandle.ForStreamId("a-3"), "value3"), x),
//				x => Assert.Equal((StreamHandle.ForHash<string>(_hasher.Hash("b")), "value2"), x));
//		}
//	}
//}
