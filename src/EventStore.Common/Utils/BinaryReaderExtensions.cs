using System;
using System.IO;

namespace EventStore.Common.Utils {
	public static class BinaryReaderExtensions {
		public static void Skip(this BinaryReader reader, params int[] bytes) =>
			reader.BaseStream.Skip(bytes);

		public static (int lengthSize,int stringSize) SkipString(this BinaryReader reader) {
			var originalPosition = reader.BaseStream.Position;
			var stringSize = ReadStringSize(reader);
			var lengthSize = (int) (reader.BaseStream.Position - originalPosition);
			reader.Skip(stringSize);
			return (lengthSize, stringSize);
		}

		public static int ReadStringSize(this BinaryReader reader) => Read7BitEncodedInt(reader);

		public static bool TryReadFull(this BinaryReader reader, byte[] buffer, int index, int count) {
			if (count == 0)
				return true;

			int read = 0;
			int rem = count;

			do {
				int cur = reader.Read(buffer, index + read, rem);
				if (cur == 0)
					return false;

				read += cur;
				rem -= cur;
			} while (read < count);

			return true;
		}

		// copied from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/binaryreader.cs
		private static int Read7BitEncodedInt(BinaryReader reader) {
			// Read out an Int32 7 bits at a time.  The high bit
			// of the byte when on means to continue reading more bytes.
			int count = 0;
			int shift = 0;
			byte b;
			do {
				// Check for a corrupted stream.  Read a max of 5 bytes.
				// In a future version, add a DataFormatException.
				if (shift == 5 * 7)  // 5 bytes max per Int32, shift += 7
					throw new FormatException();

				// ReadByte handles end of stream cases for us.
				b = reader.ReadByte();
				count |= (b & 0x7F) << shift;
				shift += 7;
			} while ((b & 0x80) != 0);
			return count;
		}
	}
}
