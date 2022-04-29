using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using EventStore.Core.Helpers;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.LogRecords {
	public static class BasicPrepareLogRecordReader {
		private const byte PrepareRecordVersion = 1;
		private static readonly ReusableBuffer _streamIdBuffer = new ReusableBuffer(256);

		public static BasicPrepareLogRecord ReadFrom(
			BinaryReader reader,
			byte version,
			long logPosition,
			ReadSpecs readSpecs)
		{
			if (version != LogRecordVersion.LogRecordV0 && version != LogRecordVersion.LogRecordV1)
				throw new ArgumentException(
					$"PrepareRecord version {version} is incorrect. Supported version: {PrepareRecordVersion}.");

			var prepareFlags = reader.ReadUInt16();

			reader.ReadInt64(); /* transaction position */
			reader.ReadInt32(); /* transaction offset */

			var expectedVersion = version == LogRecordVersion.LogRecordV0 ? reader.ReadInt32() : reader.ReadInt64();

			if (version == LogRecordVersion.LogRecordV0)
				expectedVersion = expectedVersion == int.MaxValue - 1 ? long.MaxValue - 1 : expectedVersion;

			// it is necessarily to do an early read of the stream id since reading the data/metadata depends on which
			// stream is being read. we thus use a reusable buffer for the stream id to avoid allocations.
			var streamId = ReadStreamId(reader, _streamIdBuffer);

			/* event id */
			reader.ReadInt64();
			reader.ReadInt64();

			/* correlation id */
			reader.ReadInt64();
			reader.ReadInt64();

			var timestamp = reader.ReadInt64();
			reader.SkipString(); /* event type */

			var includeData = readSpecs.IncludePrepareData(streamId, expectedVersion + 1);
			var includeMetadata = readSpecs.IncludePrepareMetadata(streamId, expectedVersion + 1);

			ReadDataOrMetadataInfo(
				reader,
				includeData,
				includeMetadata,
				out var dataSize,
				out var dataPosition,
				out var metadataSize,
				out var metadataPosition);

			// this is smaller than the actual record size but should be good enough to detect potential corruption
			// or reading at a wrong position
			if (streamId.Length + dataSize + metadataSize > TFConsts.MaxLogRecordSize)
				throw new Exception("Record too large.");

			var recordSize = CalculateSize(streamId.Length, dataSize, metadataSize);

			var basicPrepare = readSpecs.BasicPrepareRecordFactory(recordSize);
			var buffer = basicPrepare.RecordBuffer;

			var offset = 0;

			WriteHeaderToBuffer(
				buffer,
				ref offset,
				version,
				prepareFlags,
				logPosition,
				expectedVersion,
				timestamp,
				streamId.Length,
				dataSize,
				metadataSize);

			WriteStreamIdToBuffer(buffer, ref offset, streamId);
			_streamIdBuffer.Release();

			if (includeData) {
				Debug.Assert(dataPosition != -1);
				ReadBytesIntoBuffer(reader, buffer, ref offset, dataPosition, dataSize);
			}

			if (includeMetadata) {
				Debug.Assert(metadataPosition != -1);
				ReadBytesIntoBuffer(reader, buffer, ref offset, metadataPosition, metadataSize);
			}

			return basicPrepare;
		}

		private static ReadOnlySpan<byte> ReadStreamId(BinaryReader reader, ReusableBuffer buffer) {
			var streamIdSize = reader.ReadStringSize();
			var streamIdBuffer = buffer.AcquireAsByteArray(streamIdSize);
			if (!reader.TryReadFull(streamIdBuffer, 0, streamIdSize))
				throw new Exception($"Failed to read stream id.");

			return streamIdBuffer.AsSpan(0, streamIdSize);
		}

		private static void ReadDataOrMetadataInfo(
			BinaryReader reader,
			bool includeData,
			bool includeMetadata,
			out int dataSize,
			out long dataPosition,
			out int metadataSize,
			out long metadataPosition) {
			dataSize = 0;
			dataPosition = -1;
			metadataSize = 0;
			metadataPosition = -1;

			if (!includeData && !includeMetadata) return;

			// to read either the data or metadata, we necessarily need to read the data size
			dataSize = reader.ReadInt32();
			dataPosition = reader.BaseStream.Position;

			if (includeMetadata) {
				reader.Skip(dataSize);
				metadataSize = reader.ReadInt32();
				metadataPosition = reader.BaseStream.Position;
			}

			dataSize = includeData ? dataSize : 0;
		}

		private static int CalculateSize(int streamIdSize, int dataSize, int metadataSize) {
			return Marshal.SizeOf<BasicPrepareHeader>() +
			       streamIdSize +
			       dataSize +
			       metadataSize;
		}

		private static void WriteHeaderToBuffer(
			byte[] buffer,
			ref int offset,
			byte version,
			ushort prepareFlags,
			long logPosition,
			long expectedVersion,
			long timestamp,
			int streamIdSize,
			int dataSize,
			int metadataSize) {
			ref var header = ref Unsafe.As<byte, BasicPrepareHeader>(ref buffer[0]);
			header.Version = version;
			header.PrepareFlags = prepareFlags;
			header.LogPosition = logPosition;
			header.ExpectedVersion = expectedVersion;
			header.TimeStamp = timestamp;
			header.StreamIdSize = streamIdSize;
			header.DataSize = dataSize;
			header.MetadataSize = metadataSize;
			offset += Marshal.SizeOf<BasicPrepareHeader>();
		}

		private static void WriteStreamIdToBuffer(byte[] buffer, ref int offset, ReadOnlySpan<byte> streamId) {
			streamId.CopyTo(buffer.AsSpan(offset, streamId.Length));
			offset += streamId.Length;
		}

		private static void ReadBytesIntoBuffer(BinaryReader reader, byte[] buffer, ref int offset, long position, int length) {
			reader.BaseStream.Seek(position, SeekOrigin.Begin);
			if (length > 0 && !reader.TryReadFull(buffer, offset, length))
				throw new Exception($"Failed to read at position: {position}, length: {length}.");
			offset += length;
		}
	}
}
