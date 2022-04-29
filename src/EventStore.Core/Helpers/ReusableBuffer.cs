using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EventStore.Core.Helpers {
	// Thread-safe reusable buffer: similar concept as an ArrayPool<byte> but has only one buffer and one user at a time.
	// If used correctly, there should not be any contention since there can be only one user of the buffer at a time.
	// However, some synchronization is done since the AcquireAs*() and Release() methods are allowed to be called from
	// different threads.
	public class ReusableBuffer {
		private byte[] _buffer;
		private int _state;
		private int _lastSize;

		private enum State {
			Free = 0,
			LockedToAcquire = 1,
			Acquired = 2,
			LockedToRelease = 3
		}

		public ReusableBuffer(int defaultSize) {
			_buffer = new byte[defaultSize];
			_state = (int) State.Free;
			_lastSize = 0;
		}

		// Note: The acquired buffer size can be larger than the requested size
		// It is better to use AcquireAsSpan() or AcquireAsMemory() where possible.
		public byte[] AcquireAsByteArray(int size) {
			TrySwitchState(State.Free, State.LockedToAcquire);

			if (_buffer.Length < size)
				_buffer = new byte[size];

			_lastSize = size;

			TrySwitchState(State.LockedToAcquire, State.Acquired);
			return _buffer;
		}

		public Span<byte> AcquireAsSpan(int size) => AcquireAsByteArray(size).AsSpan(0, size);

		public Memory<byte> AcquireAsMemory(int size) => AcquireAsByteArray(size).AsMemory(0, size);

		public void Release() {
			TrySwitchState(State.Acquired, State.LockedToRelease);

			Array.Clear(_buffer, 0, _lastSize);
			_lastSize = 0;

			TrySwitchState(State.LockedToRelease, State.Free);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void TrySwitchState(State from, State to) {
			if (Interlocked.CompareExchange(ref _state, (int)to, (int)from) != (int)from)
				throw new InvalidOperationException($"Failed to transition from state: {from} to {to}.");
		}
	}
}
