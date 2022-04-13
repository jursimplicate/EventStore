using System;
using EventStore.Common.Utils;
using Newtonsoft.Json;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class ScavengeCheckpointJsonPersistence<TStreamId> {
		public enum Version {
			None,
			V0,
		}

		public enum Stage {
			None,
			Accumulating,
			Calculating,
			ExecutingChunks,
			Merging,
			ExecutingIndex,
			Tidying,
			Done,
		}

		public Version SchemaVersion { get; set; }
		public Stage CheckpointStage { get; set; }
		public int? DoneLogicalChunkNumber { get; set; }
		public StreamHandle<TStreamId> DoneStreamHandle { get; set; }

		public ScavengeCheckpoint ToDomain() {
			switch (CheckpointStage) {
				case Stage.Accumulating:
					return new ScavengeCheckpoint.Accumulating(DoneLogicalChunkNumber);
				case Stage.Calculating:
					return new ScavengeCheckpoint.Calculating<TStreamId>(DoneStreamHandle);
				case Stage.ExecutingChunks:
					return new ScavengeCheckpoint.ExecutingChunks(DoneLogicalChunkNumber);
				case Stage.ExecutingIndex:
					return new ScavengeCheckpoint.ExecutingIndex();
				case Stage.Done:
					return new ScavengeCheckpoint.Done();
				//qqqqqq add other cases
				default:
					throw new ArgumentOutOfRangeException(); //qq detail
			}
		}

		private static ScavengeCheckpointJsonPersistence<TStreamId> ToDto(ScavengeCheckpoint checkpoint) {
			var dto = new ScavengeCheckpointJsonPersistence<TStreamId> {
				SchemaVersion = Version.V0,
			};

			switch (checkpoint) {
				case ScavengeCheckpoint.Accumulating x:
					dto.CheckpointStage = Stage.Accumulating;
					dto.DoneLogicalChunkNumber = x.DoneLogicalChunkNumber;
					break;

				case ScavengeCheckpoint.Calculating<TStreamId> x:
					dto.CheckpointStage = Stage.Calculating;
					dto.DoneStreamHandle = x.DoneStreamHandle;
					break;

				case ScavengeCheckpoint.ExecutingChunks x:
					dto.CheckpointStage = Stage.ExecutingChunks;
					dto.DoneLogicalChunkNumber = x.DoneLogicalChunkNumber;
					break;

				case ScavengeCheckpoint.ExecutingIndex x:
					dto.CheckpointStage = Stage.ExecutingIndex;
					//qq
					break;

				case ScavengeCheckpoint.Done x:
					dto.CheckpointStage = Stage.Done;
					break;

				//qqqqqq add other cases
				default:
					throw new ArgumentOutOfRangeException(); //qq detail
			}

			return dto;
		}

		public static bool TryDeserialize(string input, out ScavengeCheckpoint checkpoint) {
			try {
				var dto = JsonConvert.DeserializeObject<ScavengeCheckpointJsonPersistence<TStreamId>>(
					input,
					Json.JsonSettings);
				checkpoint = dto.ToDomain();
				return checkpoint != null;
			} catch {
				// no op
			}

			checkpoint = default;
			return false;
		}

		public static string Serialize(ScavengeCheckpoint checkpoint) {
			var dto = ToDto(checkpoint);
			return JsonConvert.SerializeObject(dto, Json.JsonSettings);
		}
	}
}
