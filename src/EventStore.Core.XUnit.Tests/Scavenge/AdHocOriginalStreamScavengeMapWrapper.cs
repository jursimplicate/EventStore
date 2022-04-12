using System;
using System.Collections;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class AdHocOriginalStreamScavengeMapWrapper<TKey> : IOriginalStreamScavengeMap<TKey> {
		private readonly IOriginalStreamScavengeMap<TKey> _wrapped;
		private readonly Action<
			Action<TKey, DiscardPoint, DiscardPoint>,
			TKey,
			DiscardPoint,
			DiscardPoint> _f;

		public AdHocOriginalStreamScavengeMapWrapper(
			IOriginalStreamScavengeMap<TKey> wrapped,
			Action<Action<TKey, DiscardPoint, DiscardPoint>, TKey, DiscardPoint, DiscardPoint> f) {

			_wrapped = wrapped;
			_f = f;
		}

		public OriginalStreamData this[TKey key] { set => _wrapped[key] = value; }

		public IEnumerator<KeyValuePair<TKey, OriginalStreamData>> GetEnumerator() {
			return _wrapped.GetEnumerator();
		}

		public void SetDiscardPoints(TKey key, DiscardPoint discardPoint, DiscardPoint maybeDiscardPoint) {
			_f(_wrapped.SetDiscardPoints, key, discardPoint, maybeDiscardPoint);
		}

		public void SetMetadata(TKey key, StreamMetadata metadata) {
			_wrapped.SetMetadata(key, metadata);
		}

		public void SetTombstone(TKey key) {
			_wrapped.SetTombstone(key);
		}

		public bool TryGetStreamExecutionDetails(TKey key, out StreamExecutionDetails details) {
			return _wrapped.TryGetStreamExecutionDetails(key, out details);
		}

		public bool TryGetValue(TKey key, out OriginalStreamData value) {
			return _wrapped.TryGetValue(key, out value);
		}

		public bool TryRemove(TKey key, out OriginalStreamData value) {
			return _wrapped.TryRemove(key, out value);
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return ((IEnumerable)_wrapped).GetEnumerator();
		}
	}
}
