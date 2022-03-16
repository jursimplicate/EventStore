﻿using System;
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
						goto Done;

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

			Done:
			if (lastEventNumber == -1)
				throw new Exception("pokeg"); //qq not necessarily the right way to deal with this

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
