using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.LogV2;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class ScavengerTestsBase {
		protected static StreamMetadata TruncateBefore1 { get; } = new StreamMetadata(truncateBefore: 1);
		protected static StreamMetadata TruncateBefore2 { get; } = new StreamMetadata(truncateBefore: 2);
		protected static StreamMetadata TruncateBefore3 { get; } = new StreamMetadata(truncateBefore: 3);
		protected static StreamMetadata TruncateBefore4 { get; } = new StreamMetadata(truncateBefore: 4);

		protected static StreamMetadata MaxCount1 { get; } = new StreamMetadata(maxCount: 1);
		protected static StreamMetadata MaxCount2 { get; } = new StreamMetadata(maxCount: 2);
		protected static StreamMetadata MaxCount3 { get; } = new StreamMetadata(maxCount: 3);
		protected static StreamMetadata MaxCount4 { get; } = new StreamMetadata(maxCount: 4);

		protected static StreamMetadata MaxAgeMetadata { get; } =
			new StreamMetadata(maxAge: TimeSpan.FromDays(2));

		protected static DateTime EffectiveNow { get; } = new DateTime(2022, 1, 5, 00, 00, 00);
		protected static DateTime Expired { get; } = EffectiveNow - TimeSpan.FromDays(3);
		protected static DateTime Active { get; } = EffectiveNow - TimeSpan.FromDays(1);

		protected Scenario CreateScenario(Func<TFChunkDbCreationHelper, TFChunkDbCreationHelper> createDb) {
			return new Scenario(createDb);
		}

		public class Scenario {
			private readonly Func<TFChunkDbCreationHelper, TFChunkDbCreationHelper> _createDb;
			private string _hashingCancellationTrigger;
			private string _setDiscardPointsCancellationTrigger;
			private string _executingChunkCancellationTrigger;
			private string _executingIndexEntryCancellationTrigger;

			public Scenario(
				Func<TFChunkDbCreationHelper, TFChunkDbCreationHelper> createDb) {
				_createDb = createDb;
			}

			public Scenario CancelWhenHashing(string trigger) {
				_hashingCancellationTrigger = trigger;
				return this;
			}

			// note for this to work the calculator does need to set discard points for the trigger
			// stream. i.e. it needs metadata and the discard points need to move
			public Scenario CancelWhenSettingDiscardPoints(string trigger) {
				_setDiscardPointsCancellationTrigger = trigger;
				return this;
			}

			public Scenario CancelWhenExecutingChunk(string trigger) {
				_executingChunkCancellationTrigger = trigger;
				return this;
			}

			public Scenario CancelWhenExecutingIndexEntry(string trigger) {
				_executingIndexEntryCancellationTrigger = trigger;
				return this;
			}

			public async Task<ScavengeState<string>> RunAsync(
				Func<DbResult, LogRecord[][]> getExpectedKeptRecords = null,
				Func<DbResult, LogRecord[][]> getExpectedKeptIndexEntries = null) {

				return await RunInternalAsync(
					getExpectedKeptRecords,
					getExpectedKeptIndexEntries);
			}

			private async Task<ScavengeState<string>> RunInternalAsync(
				Func<DbResult, LogRecord[][]> getExpectedKeptRecords,
				Func<DbResult, LogRecord[][]> getExpectedKeptIndexEntries) {

				//qq use directory fixture, or memdb. pattern in ScavengeTestScenario.cs
				var pathName = @"unused currently";
				var dbConfig = TFChunkHelper.CreateDbConfig(pathName, 0, chunkSize: 1024 * 1024, memDb: true);

				var dbCreator = new TFChunkDbCreationHelper(dbConfig);
				var dbResult = _createDb(dbCreator).CreateDb();
				var keptRecords = getExpectedKeptRecords != null
					? getExpectedKeptRecords(dbResult)
					: null;

				var keptIndexEntries = getExpectedKeptIndexEntries != null
					? getExpectedKeptIndexEntries(dbResult)
					: keptRecords;

				// the log. will mutate as we scavenge.
				var log = dbResult.Recs;

				// original log. will not mutate, for calculating expected results.
				var originalLog = log.ToArray();

				var cancellationTokenSource = new CancellationTokenSource();
				var hasher = new HumanReadableHasher();
				var metastreamLookup = new LogV2SystemStreams();

				var collisionStorage = new InMemoryScavengeMap<string, Unit>();
				var hashesStorage = new InMemoryScavengeMap<ulong, string>();
				var metaStorage = new InMemoryScavengeMap<ulong, DiscardPoint>();
				var metaCollisionStorage = new InMemoryScavengeMap<string, DiscardPoint>();
				var originalStorage = new InMemoryOriginalStreamScavengeMap<ulong>();
				var originalCollisionStorage = new InMemoryOriginalStreamScavengeMap<string>();
				var checkpointStorage = new InMemoryScavengeMap<Unit, ScavengeCheckpoint>();
				var chunkTimeStampRangesStorage = new InMemoryScavengeMap<int, ChunkTimeStampRange>();
				var chunkWeightStorage = new InMemoryChunkWeightScavengeMap();

				var cancellationWrappedHasher = new AdHocHashWrapper<string>(
					hasher,
					(x, continuation) => {
						if (x == _hashingCancellationTrigger)
							cancellationTokenSource.Cancel();
						return continuation(x);
					});

				var cancellationWrappedOriginalStorage = new AdHocOriginalStreamScavengeMapWrapper<ulong>(
					originalStorage,
					(continuation, key, dp1, dp2) => {
						if (_setDiscardPointsCancellationTrigger != null &&
							key == hasher.Hash(_setDiscardPointsCancellationTrigger)) {

							cancellationTokenSource.Cancel();
						}
						continuation(key, dp1, dp2);
					});

				var cancellationWrappedMetastreamLookup = new AdHocMetastreamLookupWrapper<string>(
					metastreamLookup,
					(continuation, streamId) => {
						if (streamId == _executingChunkCancellationTrigger)
							cancellationTokenSource.Cancel();
						return continuation(streamId);
					});

				var indexScavenger = new ScaffoldStuffForIndexExecutor(originalLog, hasher);
				var cancellationWrappedIndexScavenger = new AdHocIndexScavengerWrapper(
					indexScavenger,
					f => entry => {
						if (_executingIndexEntryCancellationTrigger != null &&
							entry.Stream == hasher.Hash(_executingIndexEntryCancellationTrigger)) {

							cancellationTokenSource.Cancel();
						}
						return f(entry);
					});

				var scavengeState = new ScavengeState<string>(
					cancellationWrappedHasher,
					metastreamLookup,
					collisionStorage,
					hashesStorage,
					metaStorage,
					metaCollisionStorage,
					cancellationWrappedOriginalStorage,
					originalCollisionStorage,
					checkpointStorage,
					chunkTimeStampRangesStorage,
					chunkWeightStorage);

				var cancellationCheckPeriod = 1;

				var sut = new Scavenger<string>(
					scavengeState,
					new Accumulator<string>(
						metastreamLookup: metastreamLookup,
						chunkReader: new ScaffoldChunkReaderForAccumulator(log, metastreamLookup),
						cancellationCheckPeriod: cancellationCheckPeriod),
					new Calculator<string>(
						index: new ScaffoldIndexForScavenge(log, hasher),
						chunkSize: dbConfig.ChunkSize,
						cancellationCheckPeriod: cancellationCheckPeriod,
						checkpointPeriod: 1),
					new ChunkExecutor<string, ScaffoldChunk>(
						metastreamLookup: cancellationWrappedMetastreamLookup,
						chunkManager: new ScaffoldChunkManagerForScavenge(
							chunkSize: dbConfig.ChunkSize,
							log: log),
						chunkSize: dbConfig.ChunkSize,
						cancellationCheckPeriod: cancellationCheckPeriod),
					new IndexExecutor<string>(
						indexScavenger: cancellationWrappedIndexScavenger,
						streamLookup: new ScaffoldChunkReaderForIndexExecutor(log)),
					new ScaffoldScavengePointSource(log, EffectiveNow));

				await sut.RunAsync(
					new FakeTFScavengerLog(),
					cancellationTokenSource.Token);

				//qq we do some naive calculations here that are inefficient but 'obviously correct'
				// we might want to consider breaking them out and writing some simple tests for them
				// just to be sure though.

				// after loading in the log we expect to be able to
				// 1. See a list of the collisions
				// 2. Find the metadata for each stream, by stream name.
				// 3. iterate through the payloads, with a name handle for the collisions
				//    and a hashhandle for the non-collisions.

				// 1. see a list of the stream collisions
				// 1a. naively calculate list of collisions
				var hashesInUse = new Dictionary<ulong, string>();
				var collidingStreams = new HashSet<string>();

				void RegisterUse(string streamId) {
					var hash = hasher.Hash(streamId);
					if (hashesInUse.TryGetValue(hash, out var user)) {
						if (user == streamId) {
							// in use by us. not a collision.
						} else {
							// collision. register both as collisions.
							collidingStreams.Add(streamId);
							collidingStreams.Add(user);
						}
					} else {
						// hash was not in use. so it isn't a collision.
						hashesInUse[hash] = streamId;
					}
				}

				foreach (var chunk in originalLog) {
					foreach (var record in chunk) {
						if (!(record is PrepareLogRecord prepare))
							continue;

						RegisterUse(prepare.EventStreamId);

						if (metastreamLookup.IsMetaStream(prepare.EventStreamId)) {
							RegisterUse(metastreamLookup.OriginalStreamOf(prepare.EventStreamId));
						}
					}
				}

				// 1b. assert list of collisions.
				Assert.Equal(collidingStreams.OrderBy(x => x), scavengeState.Collisions().OrderBy(x => x));


				// 2. Find the metadata for each stream, by stream name
				// 2a. naively calculate the expected metadata per stream
				var expectedOriginalStreamDatas = new Dictionary<string, OriginalStreamData>();
				foreach (var chunk in originalLog) {
					foreach (var record in chunk) {
						if (!(record is PrepareLogRecord prepare))
							continue;

						if (metastreamLookup.IsMetaStream(prepare.EventStreamId)) {
							var originalStreamId = metastreamLookup.OriginalStreamOf(
								prepare.EventStreamId);

							if (metastreamLookup.IsMetaStream(originalStreamId))
								continue;

							// metadata in a metadatastream
							if (!expectedOriginalStreamDatas.TryGetValue(
								originalStreamId,
								out var data)) {

								data = OriginalStreamData.Empty;
							}

							var metadata = StreamMetadata.TryFromJsonBytes(prepare);

							data = new OriginalStreamData {
								MaxAge = metadata.MaxAge,
								MaxCount = metadata.MaxCount,
								TruncateBefore = metadata.TruncateBefore,
								IsTombstoned = data.IsTombstoned,
								//qq need? DiscardPoint = ?,
								//qq need? OriginalStreamHash = ?
							};

							expectedOriginalStreamDatas[originalStreamId] = data;
						} else if (prepare.Flags.HasAnyOf(PrepareFlags.StreamDelete)) {
							// tombstone in an original stream
							if (!expectedOriginalStreamDatas.TryGetValue(
								prepare.EventStreamId,
								out var data)) {

								data = OriginalStreamData.Empty;
							}

							data = new OriginalStreamData {
								MaxAge = data.MaxAge,
								MaxCount = data.MaxCount,
								TruncateBefore = data.TruncateBefore,
								IsTombstoned = true,
							};

							expectedOriginalStreamDatas[prepare.EventStreamId] = data;
						}
					}
				}

				// 2b. assert that we can find each one
				//qq should also check that we dont have any extras
				var compareOnlyMetadata = new CompareOnlyMetadataAndTombstone();
				foreach (var kvp in expectedOriginalStreamDatas) {
					Assert.True(
						scavengeState.TryGetOriginalStreamData(kvp.Key, out var originalStreamData),
						$"could not find metadata for stream {kvp.Key}");
					Assert.Equal(kvp.Value, originalStreamData, compareOnlyMetadata);
				}

				// 3. Iterate through the metadatas, find the appropriate handles.
				// 3a. naively calculate the expected handles. one for each metadata, some by hash,
				// some by streamname
				//qq can/should probably check the handles of the metastream discard points too
				var expectedHandles = expectedOriginalStreamDatas
					.Select(kvp => {
						var stream = kvp.Key;
						var metadata = kvp.Value;

						return collidingStreams.Contains(stream)
							? (StreamHandle.ForStreamId(stream), metadata)
							: (StreamHandle.ForHash<string>(hasher.Hash(stream)), metadata);
					})
					.Select(x => (x.Item1.ToString(), x.metadata))
					.OrderBy(x => x.Item1);

				// 3b. compare to the actual handles.
				var actual = scavengeState
					.OriginalStreamsToScavenge
					.Select(x => (x.Item1.ToString(), x.Item2))
					.OrderBy(x => x.Item1);

				// compare the handles
				Assert.Equal(expectedHandles.Select(x => x.Item1), actual.Select(x => x.Item1));

				// compare the metadatas
				Assert.Equal(
					expectedHandles.Select(x => x.metadata),
					actual.Select(x => x.Item2),
					compareOnlyMetadata);

				// 4. The records we expected to keep are kept
				// 5. The index entries we expected to be kept are kept
				if (keptRecords != null) {
					CheckRecordsScaffolding(keptRecords, dbResult);
					CheckIndex(keptIndexEntries, indexScavenger.Scavenged);
				}

				return scavengeState;
			}

			//qq nicked from scavengetestscenario, will probably just use that class
			protected static void CheckRecords(LogRecord[][] expected, DbResult actual) {
				Assert.True(
					expected.Length == actual.Db.Manager.ChunksCount,
					"Wrong number of chunks. " +
					$"Expected {expected.Length}. Actual {actual.Db.Manager.ChunksCount}");

				for (int i = 0; i < expected.Length; ++i) {
					var chunk = actual.Db.Manager.GetChunk(i);

					var chunkRecords = new List<LogRecord>();
					var result = chunk.TryReadFirst();
					while (result.Success) {
						chunkRecords.Add(result.LogRecord);
						result = chunk.TryReadClosestForward((int)result.NextPosition);
					}

					Assert.True(
						expected[i].Length == chunkRecords.Count,
						$"Wrong number of records in chunk #{i}. " +
						$"Expected {expected[i].Length}. Actual {chunkRecords.Count}");

					for (int j = 0; j < expected[i].Length; ++j) {
						Assert.True(
							expected[i][j] == chunkRecords[j],
							$"Wrong log record #{j} read from chunk #{i}. " +
							$"Expected {expected[i][j]}. Actual {chunkRecords[j]}");
					}
				}
			}

			//qq this one reads the records out of actual.Recs, for use with the scaffolding implementations
			// until we transition.
			protected static void CheckRecordsScaffolding(LogRecord[][] expected, DbResult actual) {
				Assert.True(
					expected.Length == actual.Db.Manager.ChunksCount,
					"Wrong number of chunks. " +
					$"Expected {expected.Length}. Actual {actual.Db.Manager.ChunksCount}");

				for (int i = 0; i < expected.Length; ++i) {
					var chunkRecords = actual.Recs[i].ToList();

					Assert.True(
						expected[i].Length == chunkRecords.Count,
						$"Wrong number of records in chunk #{i}. " +
						$"Expected {expected[i].Length}. Actual {chunkRecords.Count}");

					for (int j = 0; j < expected[i].Length; ++j) {
						Assert.True(
							expected[i][j].Equals(chunkRecords[j]),
							$"Wrong log record #{j} read from chunk #{i}.\r\n" +
							$"Expected {expected[i][j]}\r\n" +
							$"Actual   {chunkRecords[j]}");
					}
				}
			}

			private static void CheckIndex(LogRecord[][] expected, LogRecord[][] actual) {
				Assert.True(
					expected.Length == actual.Length,
					"IndexCheck. Wrong number of index-chunks. " +
					$"Expected {expected.Length}. Actual {actual.Length}");

				for (int i = 0; i < expected.Length; ++i) {
					var chunkRecords = actual[i];

					Assert.True(
						expected[i].Length == chunkRecords.Length,
						$"IndexCheck. Wrong number of records in index-chunk #{i}. " +
						$"Expected {expected[i].Length}. Actual {chunkRecords.Length}");

					for (int j = 0; j < expected[i].Length; ++j) {
						Assert.True(
							expected[i][j].Equals(chunkRecords[j]),
							$"IndexCheck. Wrong log record #{j} read from index-chunk #{i}.\r\n" +
							$"Expected {expected[i][j]}\r\n" +
							$"Actual   {chunkRecords[j]}");
					}
				}
			}

			class CompareOnlyMetadataAndTombstone : IEqualityComparer<OriginalStreamData> {
				public bool Equals(OriginalStreamData x, OriginalStreamData y) {
					if ((x == null) != (y == null))
						return false;

					if (x is null)
						return true;

					return
						x.IsTombstoned == y.IsTombstoned &&
						x.MaxCount == y.MaxCount &&
						x.MaxAge == y.MaxAge &&
						x.TruncateBefore == y.TruncateBefore;
				}

				public int GetHashCode(OriginalStreamData obj) {
					throw new NotImplementedException();
				}
			}
		}
	}

}
