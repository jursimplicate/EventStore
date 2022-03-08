namespace EventStore.Core.TransactionLog.Scavenging {
	public readonly struct DiscardPoint {
		//qq do we need as many bits as this
		public readonly long Value;

		private DiscardPoint(long value) {
			Value = value;
		}

		//qq make sure this is correct wrt the semantics of the Value
		public static readonly DiscardPoint Tombstone = new(long.MaxValue);

		//qq check semantic
		// keeps all events
		public static readonly DiscardPoint KeepAll = new(0);

		//qq get this right wrt inclusive/exclusive. name? DiscardBefore ? 
		public static DiscardPoint DiscardBefore(long value) {
			return new(value);
		}

		public static DiscardPoint DiscardIncluding(long value) {
			//qq if this is in range
			return DiscardBefore(value + 1);
		}
		// Produces a discard point that discards when any of the provided discard points would discard
		// i.e. takes the bigger of the two.
		public static DiscardPoint AnyOf(DiscardPoint x, DiscardPoint y) {
			return x.Value > y.Value ? x : y;
		}

		//qq property? consider name and semantics
		public bool IsNotMax() {
			return Value < long.MaxValue;
		}

		//qq depends on whether the DP is the first event to keep
		// or the last event to discard. which in turn will depend on which is easier to generate
		// consider the edges of the range actually. tombstone is long.max and usually we would/
		// keep the tombstone but there is an option to discard it, in which case the value should
		// presumably be long.max and it should be the last event to discard.
		// we also need a way to express that we want to keep all the events. 
		//qq we do need to have a discard point that means 'discard nothing'
		public bool ShouldDiscard(long eventNumber) =>
			eventNumber < Value;
	}

	//qq note, probably need to complain if the ptable is a 32bit table
	//qq maybe we need a collision free core that is just a map
	//qq just a thought, lots of the metadatas might be equal, we might be able to store each uniqueb
	// instance once. implementation detail.
}
