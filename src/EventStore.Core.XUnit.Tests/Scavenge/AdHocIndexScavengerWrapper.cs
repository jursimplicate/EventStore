using System;
using System.Threading;
using EventStore.Core.Index;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class AdHocIndexScavengerWrapper : IIndexScavenger {
		private readonly IIndexScavenger _wrapped;
		private readonly Func<Func<IndexEntry, bool>, Func<IndexEntry, bool>> _f;

		public AdHocIndexScavengerWrapper(
			IIndexScavenger wrapped,
			Func<Func<IndexEntry, bool>, Func<IndexEntry, bool>> f) {

			_wrapped = wrapped;
			_f = f;
		}

		public void ScavengeIndex(
			Func<IndexEntry, bool> shouldKeep,
			IIndexScavengerLog log,
			CancellationToken cancellationToken) {

			_wrapped.ScavengeIndex(_f(shouldKeep), log, cancellationToken);
		}
	}
}
