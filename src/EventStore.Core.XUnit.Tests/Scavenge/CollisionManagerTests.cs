using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	// we accumulate metadata per stream and check that it worked
	//qq we may be able to get rid of these tests when we have high level tests
	//qq indeed when we have these tests we may be able to get rid of the
	// collision resolver and detector tests
	public class CollisionManagerTests {
		private class Record {
			public Record(string streamName, string metadata = null) {
				StreamName = streamName;
				Metadata = metadata;
			}

			public string StreamName { get; }
			public string Metadata { get; }
		}

		[Fact]
		public void Trivial() {
			RunScenario(
				// the first letter of the stream name determines its hash value
				// a-1:       a stream called "a-1" which hashes to "a"
				new Record("a-1"),
				// setting metadata for a-1, which does not collide with a-1
				new Record("b-$$a-1", "meta1"));
		}

		[Fact]
		public void seen_stream_before() {
			RunScenario(
				new Record("a-1"),
				new Record("a-1"));
		}

		[Fact]
		public void collision() {
			RunScenario(
				new Record("a-1"),
				new Record("a-2"));
		}

		[Fact]
		public void metadata_non_colliding() {
			RunScenario(
				new Record("a-1"),
				new Record("b-$$a-1", "metadata1"));
		}

		//qq now that we are keying on the metadta streams, does that mean that we don't
		// need to many cases here? like whether or not the original streams collide might not be
		// relevant any more.
		//
		//qqqqqqqqqqqqq do we want to bake tombstones into here as well
		[Fact]
		public void metadata_colliding() {
			RunScenario(
				new Record("a-1"),
				new Record("a-$$a-1", "metadata1"));
		}

		[Fact]
		public void metadatas_for_different_streams_non_colliding() {
			RunScenario(
				new Record("A-$$a-1", "metadata1"),
				new Record("B-$$b-2", "metadata2"));
		}

		[Fact]
		public void metadatas_for_different_streams_all_colliding() {
			RunScenario(
				new Record("a-$$a-1", "metadata1"),
				new Record("a-$$a-2", "metadata2"));
		}

		[Fact]
		public void metadatas_for_different_streams_original_streams_colliding() {
			RunScenario(
				new Record("A-$$a-1", "metadata1"),
				new Record("B-$$a-2", "metadata2"));
		}

		[Fact]
		public void metadatas_for_different_streams_meta_streams_colliding() {
			RunScenario(
				new Record("A-$$a-1", "metadata1"),
				new Record("A-$$b-2", "metadata2"));
		}

		[Fact]
		public void metadatas_for_different_streams_original_and_meta_colliding() {
			RunScenario(
				new Record("A-$$a-1", "metadata1"),
				new Record("A-$$a-2", "metadata2"));
		}

		[Fact]
		public void metadatas_for_different_streams_cross_colliding() {
			RunScenario(
				new Record("b-$$a-1", "metadata1"),
				new Record("a-$$b-2", "metadata2"));
		}

		//qq need to test promotion

		private void RunScenario(params Record[] log) {
			var hasher = new FirstCharacterHasher();
			var metastreamLookup = new MockMetastreamLookup();

			bool HashInUseBefore(string streamName, long position, out string hashUser) {
				var hash = hasher.Hash(streamName);
				for (int i = 0; i < position; i++) {
					var record = log[i];
					if (hasher.Hash(record.StreamName) == hash) {
						hashUser = record.StreamName;
						return true;
					}
				}

				hashUser = default;
				return false;
			}

			var sut = new CollisionManager<string, string>(
				hasher,
				HashInUseBefore,
				new InMemoryCollisionResolver<string, string>(hasher));

			// iterate through the log, detecting collisions and accumulating metadatas
			for (var i = 0; i < log.Length; i++) {
				var record = log[i];
				sut.DetectCollisions(record.StreamName, i);

				if (metastreamLookup.IsMetaStream(record.StreamName)) {
					sut[record.StreamName] = record.Metadata;
				}
			}

			// after loading in the log we expect to be able to
			// 1. See a list of the collisions
			// 2. Find the metadata for each stream, by stream name.
			// 3. iterate through the payloads, with a name handle for the collisions
			//    and a hashhandle for the non-collisions.

			//qq probably we want to factor some of this into a naive/mock class that can itself be tested to make
			// sure that it works right. then compare the results of that to the real (efficient) implementation.

			// 1. see a list of the stream collisions
			// 1a. calculate list of collisions
			var hashesInUse = new Dictionary<ulong, string>();
			var collidingStreams = new HashSet<string>();
			foreach (var record in log) {
				var hash = hasher.Hash(record.StreamName);
				if (hashesInUse.TryGetValue(hash, out var user)) {
					if (user == record.StreamName) {
						// in use by us. not a collision.
					} else {
						// collision. register both as collisions.
						collidingStreams.Add(record.StreamName);
						collidingStreams.Add(user);
					}
				} else {
					// hash is not in use. so it isn't a collision.
					hashesInUse[hash] = record.StreamName;
				}
			}

			// 1b. assert list of collisions.
			Assert.Equal(collidingStreams.OrderBy(x => x), sut.Collisions().OrderBy(x => x));


			// 2. Find the metadata for each stream, by stream name
			// 2a. calculated the expected metadata per stream
			var expectedMetadataPerStream = new Dictionary<string, string>();
			foreach (var record in log) {
				if (record.Metadata is not null) {
					expectedMetadataPerStream[record.StreamName] = record.Metadata;
				}
			}

			// 2b. aseert that we can find each one
			foreach (var kvp in expectedMetadataPerStream) {
				Assert.True(sut.TryGetValue(kvp.Key, out var value));
				Assert.Equal(kvp.Value, value);
			}

			// 3. Iterate through the metadatas, find the appropriate handles.
			// 3a. calculate the expected handles. one for each metadata, some by hash, some by streamname
			var expectedHandles = expectedMetadataPerStream
				.Select(kvp => {
					var stream = kvp.Key;
					var metadata = kvp.Value;

					return collidingStreams.Contains(stream)
						? (StreamHandle.ForStreamId(stream), metadata)
						: (StreamHandle.ForHash<string>(hasher.Hash(stream)), metadata);
				})
				.Select(x => x.ToString())
				.OrderBy(x => x);

			// 3b. compare to the actual handles.
			var actual = sut
				.Enumerate()
				.Select(x => x.ToString())
				.OrderBy(x => x);

			Assert.Equal(expectedHandles, actual);
		}

		class HandleComparer : IEqualityComparer<StreamHandle<string>> {
			public bool Equals(StreamHandle<string> x, StreamHandle<string> y) {
				return false;
			}

			public int GetHashCode([DisallowNull] StreamHandle<string> obj) {
				throw new NotImplementedException();
			}
		}

	}
}
