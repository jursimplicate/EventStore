using System.IO;
using System.Linq;

namespace EventStore.Common.Utils {
	public static class StreamExtensions {
		public static void Skip(this Stream stream, params int[] bytes) =>
			stream.Seek(bytes.Sum(), SeekOrigin.Current);
	}
}
