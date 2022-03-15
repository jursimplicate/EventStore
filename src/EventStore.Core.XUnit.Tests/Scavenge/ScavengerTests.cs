using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class ScavengerTests {
		private static readonly MetastreamData _meta1 = new() { MaxCount = 1 };
		private static readonly MetastreamData _meta2 = new() { MaxCount = 2 };

		[Fact]
		public void Trivial() {
			RunScenario(
				// the first letter of the stream name determines its hash value
				// a-1:       a stream called "a-1" which hashes to "a"
				new MockRecord("a-1"),
				// setting metadata for a-1, which does not collide with a-1
				new MockRecord("b-$$a-1", _meta1));
		}

		[Fact]
		public void seen_stream_before() {
			RunScenario(
				new MockRecord("a-1"),
				new MockRecord("a-1"));
		}

		[Fact]
		public void collision() {
			RunScenario(
				new MockRecord("a-1"),
				new MockRecord("a-2"));
		}

		[Fact]
		public void metadata_non_colliding() {
			RunScenario(
				new MockRecord("a-1"),
				new MockRecord("B-$$a-1", _meta1));
		}

		//qq now that we are keying on the metadta streams, does that mean that we don't
		// need to many cases here? like whether or not the original streams collide might not be
		// relevant any more.
		//
		//qqqqqqqqqqqqq do we want to bake tombstones into here as well
		[Fact]
		public void metadata_colliding() {
			RunScenario(
				new MockRecord("a-1"),
				new MockRecord("a-$$a-1", _meta1));
		}

		//qq this would fail if we checked that looking up the metadatas per stream gives us
		// the right metadatas. a-2 has no metadata but we would find _meta1 anyway.
		[Fact]
		public void darn() {
			RunScenario(
				new MockRecord("A-$$a-1", _meta1),
				new MockRecord("a-2"));
		}

		[Fact]
		public void metadatas_for_different_streams_non_colliding() {
			RunScenario(
				new MockRecord("A-$$a-1", _meta1),
				new MockRecord("B-$$b-2", _meta2));
		}

		[Fact]
		public void metadatas_for_different_streams_all_colliding() {
			RunScenario(
				new MockRecord("a-$$a-1", _meta1),
				new MockRecord("a-$$a-2", _meta2));
		}

		[Fact]
		public void metadatas_for_different_streams_original_streams_colliding() {
			RunScenario(
				new MockRecord("A-$$a-1", _meta1),
				new MockRecord("B-$$a-2", _meta2));
		}

		[Fact]
		public void metadatas_for_different_streams_meta_streams_colliding() {
			RunScenario(
				new MockRecord("A-$$a-1", _meta1),
				new MockRecord("A-$$b-2", _meta2));
		}

		[Fact]
		public void metadatas_for_different_streams_original_and_meta_colliding() {
			RunScenario(
				new MockRecord("A-$$a-1", _meta1),
				new MockRecord("A-$$a-2", _meta2));
		}

		[Fact]
		public void metadatas_for_different_streams_cross_colliding() {
			RunScenario(
				new MockRecord("b-$$a-1", _meta1),
				new MockRecord("a-$$b-2", _meta2));
		}

		private class MockScavengePointSource : IScavengePointSource {
			public ScavengePoint GetScavengePoint() {
				throw new NotImplementedException();
			}
		}

		//qq refactor to base class
		private static void RunScenario(params MockRecord[] log) {
			var hasher = new FirstCharacterHasher();
			var metastreamLookup = new MockMetastreamLookup();

			var collisionStorage = new InMemoryScavengeMap<string, Unit>();
			var metaStorage = new InMemoryScavengeMap<ulong, MetastreamData>();
			var metaCollisionStorage = new InMemoryScavengeMap<string, MetastreamData>();
			var originalStorage = new InMemoryScavengeMap<ulong, DiscardPoint>();
			var originalCollisionStorage = new InMemoryScavengeMap<string, DiscardPoint>();

			var scavengeState = new ScavengeState<string>(
				hasher,
				collisionStorage,
				metaStorage,
				metaCollisionStorage,
				originalStorage,
				originalCollisionStorage,
				new MockIndexReaderForAccumulator(hasher, log));

			var sut = new Scavenger<string>(
				scavengeState,
				new Accumulator<string>(
					hasher: hasher,
					metastreamLookup: metastreamLookup,
					chunkReader: new MockChunkReaderForAccumulator<string>(log)),
				new Calculator<string>(
					index: new MockIndexForScavenge(log)),
				new ChunkExecutor<string>(
					chunkManager: new MockChunkManagerForScavenge(),
					chunkReader: new MockChunkReaderForScavenge(log)),
				new IndexExecutor<string>(),
				new MockScavengePointSource());

			sut.Start(); //qq irl how do we know when its done
			// iterate through the log, detecting collisions and accumulating metadatas
			//qqqqqqqq this is what the accumulator will do.
			for (var i = 0; i < log.Length; i++) {
				var record = log[i];
				scavengeState.DetectCollisions(record.StreamName, i);

				if (metastreamLookup.IsMetaStream(record.StreamName)) {
					scavengeState.SetMetastreamData(record.StreamName, record.MetastreamData);
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
			Assert.Equal(collidingStreams.OrderBy(x => x), scavengeState.Collisions().OrderBy(x => x));


			// 2. Find the metadata for each stream, by stream name
			// 2a. calculated the expected metadata per stream
			var expectedMetadataPerStream = new Dictionary<string, MetastreamData>();
			foreach (var record in log) {
				if (record.MetastreamData is not null) {
					expectedMetadataPerStream[record.StreamName] = record.MetastreamData;
				}
			}

			// 2b. aseert that we can find each one
			foreach (var kvp in expectedMetadataPerStream) {
				var meta = scavengeState.GetMetastreamData(kvp.Key);
				Assert.Equal(kvp.Value, meta);
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
			var actual = scavengeState
				.MetastreamDatas
				.Select(x => x.ToString())
				.OrderBy(x => x);

			Assert.Equal(expectedHandles, actual);
		}
	}
}
