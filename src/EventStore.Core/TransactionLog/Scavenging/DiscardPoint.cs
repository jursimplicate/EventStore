using EventStore.Common.Utils;

namespace EventStore.Core.TransactionLog.Scavenging {
	public readonly struct DiscardPoint {
		private DiscardPoint(long lastEventNumberToDiscard) {
			LastEventNumberToDiscard = lastEventNumberToDiscard;
		}

		public static DiscardPoint DiscardIncluding(long eventNumber) =>
			new(eventNumber);

		public static DiscardPoint DiscardBefore(long eventNumber) =>
			DiscardIncluding(eventNumber - 1);

		public static DiscardPoint KeepAll { get; } = DiscardBefore(0);

		public static DiscardPoint Tombstone { get; } = DiscardBefore(long.MaxValue);

		public static DiscardPoint DiscardAll { get; } = DiscardIncluding(long.MaxValue);

		//qq do we need as many bits as this
		public long LastEventNumberToDiscard { get; }

		// Produces a discard point that discards when any of the provided discard points would discard
		// i.e. takes the bigger of the two.
		public static DiscardPoint AnyOf(DiscardPoint x, DiscardPoint y) {
			return x.LastEventNumberToDiscard > y.LastEventNumberToDiscard ? x : y;
		}

		public static bool operator ==(DiscardPoint x, DiscardPoint y) =>
			x.LastEventNumberToDiscard == y.LastEventNumberToDiscard;

		public static bool operator !=(DiscardPoint x, DiscardPoint y) =>
			x.LastEventNumberToDiscard != y.LastEventNumberToDiscard;

		public override bool Equals(object obj) =>
			obj is DiscardPoint that && this == that;

		public override int GetHashCode() =>
			LastEventNumberToDiscard.GetHashCode();

		public bool ShouldDiscard(long eventNumber) {
			Ensure.Nonnegative(eventNumber, nameof(eventNumber));
			return eventNumber <= LastEventNumberToDiscard;
		}
	}
}
