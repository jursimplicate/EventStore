using System;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class AdHocHashWrapper<T> : ILongHasher<T> {
		private readonly ILongHasher<T> _wrapped;
		private readonly Func<T, Func<T, ulong>, ulong> _f;

		public AdHocHashWrapper(ILongHasher<T> wrapped, Func<T, Func<T, ulong>, ulong> f) {
			_wrapped = wrapped;
			_f = f;
		}

		public ulong Hash(T x) => _f(x, _wrapped.Hash);
	}
}
