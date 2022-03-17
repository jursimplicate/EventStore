using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Index.Hashes;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	//qq the scaffold classes help us to get things tested before we have the real implementations
	// written, but will be removed once we can drop in the real implementations (which can run against
	// memdb for rapid testing)
	public class ScaffoldIndexReaderForAccumulator : IIndexReaderForAccumulator<string> {
		private readonly ILongHasher<string> _hasher;
		private readonly ILogRecord[][] _log;

		public ScaffoldIndexReaderForAccumulator(
			ILongHasher<string> hasher, ILogRecord[][] log) {

			_hasher = hasher;
			_log = log;
		}

		public bool HashInUseBefore(ulong hash, long position, out string hashUser) {
			// iterate through the log

			foreach (var chunk in _log) {
				foreach (var record in chunk) {
					if (record.LogPosition >= position) {
						hashUser = default;
						return false;
					}

					switch (record) {
						//qq technically probably only have to detect in use if its committed
						// but this is a detail that probalby wont matter for us
						case IPrepareLogRecord<string> prepare: {
								//qq do these have populated event numbres? what about when committed?
							var stream = prepare.EventStreamId;
							if (_hasher.Hash(stream) == hash) {
								hashUser = stream;
								return true;
							}
							break;
						}
						//qq any other record types use streams?
						default:
							break;
					}
				}
			}

			hashUser = default;
			return false;
		}
	}

	public class ScaffoldScavengePointSource : IScavengePointSource {
		private readonly ScavengePoint _scavengePoint;

		public ScaffoldScavengePointSource(ScavengePoint scavengePoint) {
			_scavengePoint = scavengePoint;
		}

		public ScavengePoint GetScavengePoint() {
			return _scavengePoint;
		}
	}

	public class ScaffoldChunkReaderForAccumulator : IChunkReaderForAccumulator<string> {
		private readonly ILogRecord[][] _log;

		public ScaffoldChunkReaderForAccumulator(ILogRecord[][] log) {
			_log = log;
		}

		public IEnumerable<RecordForAccumulator<string>> Read(
			int startFromChunk,
			ScavengePoint scavengePoint) {

			var stopBefore = scavengePoint.Position;

			for (int chunkIndex = startFromChunk; chunkIndex < _log.Length; chunkIndex++) {
				var chunk = _log[chunkIndex];
				foreach (var record in chunk) {
					if (record.LogPosition >= stopBefore)
						yield break;

					if (record is not IPrepareLogRecord<string> prepare)
						continue;

					//qq in each case what is the sufficient condition
					// do we worry about whether a user might have created system events
					// in the wrong place, or with the wrong event number, etc.

					if (prepare.EventType == SystemEventTypes.StreamMetadata) {
						yield return new RecordForAccumulator<string>.MetadataRecord {
							EventNumber = prepare.ExpectedVersion + 1,
							LogPosition = prepare.LogPosition,
							Metadata = StreamMetadata.FromJsonBytes(prepare.Data),
							StreamId = prepare.EventStreamId,
						};
					} else if (prepare.EventType == SystemEventTypes.StreamDeleted) {
						//qq maybe the eventtype is enough. if we also check the expected version then
						// we'd want to be careful about prepare version 0 tombstones.
						// is the StreamDelete prepare flag relevant?
						//qq does anywhere else in the scavenge logic rely on the tombstone
						// having a particular expected version
						// does a tombstone get indexed, are we expecting to find it in the index
						// it would mean that we can't just just set the DiscardPoint to DP.Tombstone
						// and have it discard events that are before then if the tombstone is, in fact
						// before then! what does old scavenge do? old scavenge uses the prepare flag
						// to detect that it is a tombstone and keep it. we can do that in the log
						// but can't do that index only.
						// it looooks like the indexreader knows a stream is deleted by seeing its
						// event number is EventNumber.DeletedStream i.e. long.max
						// so maybe the event number is the thing that matters. presumably this works
						// for old prepares too...... aha in the index reader the latest version number
						//  _always_ comes from the index and not from the record itself, so perhaps we
						// upgraded the maxvalue as part of upgrading the ptable
						// in which case the index entry for the tombstone might have a different
						// eventnumber to the expected version of the record in the log.
						// hmm!

						yield return new RecordForAccumulator<string>.TombStoneRecord {
							LogPosition = prepare.LogPosition,
							StreamId = prepare.EventStreamId,
						};
					} else {
						yield return new RecordForAccumulator<string>.EventRecord {
							LogPosition = prepare.LogPosition,
							StreamId = prepare.EventStreamId,
						};
					}
				}
			}
		}
	}


	public class ScaffoldIndexForScavenge : IIndexReaderForCalculator<string> {
		private readonly ILogRecord[][] _log;
		private readonly ILongHasher<string> _hasher;

		public ScaffoldIndexForScavenge(ILogRecord[][] log, ILongHasher<string> hasher) {
			_log = log;
			_hasher = hasher;
		}

		public long GetLastEventNumber(StreamHandle<string> streamHandle, ScavengePoint scavengePoint) {
			var lastEventNumber = -1L;
			//qq technically should only to consider committed prepares but probably doesn't matter
			// for our purposes here.
			var stopBefore = scavengePoint.Position;
			foreach (var chunk in _log) {
				foreach (var record in chunk) {
					if (record.LogPosition >= stopBefore)
						return lastEventNumber;

					if (record is not IPrepareLogRecord<string> prepare)
						continue;

					if (streamHandle.IsHash) {
						if (_hasher.Hash(prepare.EventStreamId) == streamHandle.StreamHash) {
							lastEventNumber = prepare.ExpectedVersion + 1;
						}
					} else {
						if (prepare.EventStreamId == streamHandle.StreamId) {
							lastEventNumber = prepare.ExpectedVersion + 1;
						}
					}
				}
			}

			return lastEventNumber;
		}

		public EventInfo[] ReadEventInfoForward(
			StreamHandle<string> stream,
			long fromEventNumber,
			int maxCount,
			ScavengePoint scavengePoint) {

			var result = new List<EventInfo>();

			var stopBefore = scavengePoint.Position;

			foreach (var chunk in _log) {
				foreach (var record in chunk) {
					if (record.LogPosition >= stopBefore)
						goto Done;

					if (record is not IPrepareLogRecord<string> prepare)
						continue;

					//qqqqqqqqq filter according to stream
					result.Add(new EventInfo(prepare.LogPosition, prepare.ExpectedVersion + 1));
				}
			}

			Done:
			return result.ToArray();
		}
	}

	public class ScaffoldChunkReaderForScavenge : IChunkReaderForChunkExecutor<string> {
		public ScaffoldChunkReaderForScavenge(ILogRecord[][] log) {
		}

		public IEnumerable<RecordForScavenge<string>> Read(TFChunk chunk) {
			yield return new() {
				StreamId = "thestream",
				EventNumber = 123,
			};
		}
	}

	public class ScaffoldChunkManagerForScavenge : IChunkManagerForChunkExecutor {
		public TFChunk GetChunk(int logicalChunkNum) {
			throw new NotImplementedException();
		}

		public TFChunk SwitchChunk(TFChunk chunk, bool verifyHash, bool removeChunksWithGreaterNumbers) {
			throw new NotImplementedException();
		}
	}

	//qq
	public class ScaffoldStuffForIndexExecutor : IDoStuffForIndexExecutor {
	}
}
