using System;
using System.Runtime.InteropServices;
using EventStore.Core.Helpers;

namespace EventStore.Core.TransactionLog.LogRecords {
	[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 44)]
	public struct BasicPrepareHeader {
		[FieldOffset(0)]
		public byte Version;
		[FieldOffset(2)]
		public ushort PrepareFlags;
		[FieldOffset(8)]
		public long LogPosition;
		[FieldOffset(16)]
		public long ExpectedVersion;
		[FieldOffset(24)]
		public long TimeStamp;
		[FieldOffset(32)]
		public int StreamIdSize;
		[FieldOffset(36)]
		public int DataSize;
		[FieldOffset(40)]
		public int MetadataSize;
	}

	public class BasicPrepareLogRecord : LogRecord, IReusableObject, IDisposable {
		public new byte Version => RecordBuffer[0];
		public new long LogPosition => BitConverter.ToInt64(RecordBuffer, 8);
		public PrepareFlags PrepareFlags => (PrepareFlags)BitConverter.ToUInt16(RecordBuffer, 2);
		public ReadOnlyMemory<byte> EventStreamId => RecordBuffer.AsMemory().Slice(StreamIdOffset, StreamIdSize);
		public long ExpectedVersion => BitConverter.ToInt64(RecordBuffer, 16);
		public DateTime TimeStamp => new DateTime(BitConverter.ToInt64(RecordBuffer, 24));
		public ReadOnlyMemory<byte> Data => RecordBuffer.AsMemory().Slice(DataOffset, DataSize);
		public ReadOnlyMemory<byte> Metadata => RecordBuffer.AsMemory().Slice(MetadataOffset, MetadataSize);

		private int StreamIdSize => BitConverter.ToInt32(RecordBuffer, 32);
		private int StreamIdOffset => 44;
		private int DataSize => BitConverter.ToInt32(RecordBuffer, 36);
		private int DataOffset => 44 + StreamIdSize;
		private int MetadataSize => BitConverter.ToInt32(RecordBuffer, 40);
		private int MetadataOffset => 44 + StreamIdSize + DataSize;

		public byte[] RecordBuffer { get; private set; }
		private Action OnDispose { get; set; }

		public BasicPrepareLogRecord() : base(LogRecordType.Prepare, 0, 0) { }

		public void Initialize(IReusableObjectInitParams initParams) {
			var p = (BasicPrepareInitParams)initParams;
			RecordBuffer = p.RecordBuffer;
			OnDispose = p.OnDispose;
		}

		public void Reset() {
			RecordBuffer = default;
			OnDispose = default;
		}

		public void Dispose() {
			OnDispose?.Invoke();
		}

		public override string ToString() {
			return $"LogPosition: {LogPosition}, " +
			       $"Flags: {PrepareFlags}, " +
			       $"EventStreamId: {EventStreamId}, " +
			       $"ExpectedVersion: {ExpectedVersion}, " +
			       $"TimeStamp: {TimeStamp}, " +
			       $"Data size: {Data.Length}, " +
			       $"Metadata size: {Metadata.Length}";
		}
	}

	public struct BasicPrepareInitParams : IReusableObjectInitParams {
		public readonly byte[] RecordBuffer;
		public readonly Action OnDispose;

		public BasicPrepareInitParams(byte[] recordBuffer, Action onDispose) {
			RecordBuffer = recordBuffer;
			OnDispose = onDispose;
		}
	}
}
