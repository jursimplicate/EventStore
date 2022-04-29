using System;

namespace EventStore.Core.TransactionLog {
	public struct RentedMemory {
		public readonly byte[] Buffer;
		public readonly Action<byte[]> Release;

		public RentedMemory(byte[] buffer, Action<byte[]> release) {
			Buffer = buffer;
			Release = release;
		}
	}
}
