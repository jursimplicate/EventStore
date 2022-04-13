using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class ScavengeStateBuilder {
		private readonly ILongHasher<string> _hasher;
		private readonly IMetastreamLookup<string> _metastreamLookup;

		private ScavengeState<string> _preexisting;

		public ScavengeStateBuilder(
			ILongHasher<string> hasher,
			IMetastreamLookup<string> metastreamLookup) {

			_hasher = hasher;
			_metastreamLookup = metastreamLookup;
		}

		public ScavengeStateBuilder ExistingState(ScavengeState<string> state) {
			_preexisting = state;
			return this;
		}

		public ScavengeState<string> Build() {
			if (_preexisting != null)
				return _preexisting;

			var collisionStorage = new InMemoryScavengeMap<string, Unit>();
			var hashesStorage = new InMemoryScavengeMap<ulong, string>();
			var metaStorage = new InMemoryScavengeMap<ulong, DiscardPoint>();
			var metaCollisionStorage = new InMemoryScavengeMap<string, DiscardPoint>();
			var originalStorage = new InMemoryOriginalStreamScavengeMap<ulong>();
			var originalCollisionStorage = new InMemoryOriginalStreamScavengeMap<string>();
			var checkpointStorage = new InMemoryScavengeMap<Unit, ScavengeCheckpoint>();
			var chunkTimeStampRangesStorage = new InMemoryScavengeMap<int, ChunkTimeStampRange>();
			var chunkWeightStorage = new InMemoryChunkWeightScavengeMap();

			var scavengeState = new ScavengeState<string>(
				_hasher,
				_metastreamLookup,
				collisionStorage,
				hashesStorage,
				metaStorage,
				metaCollisionStorage,
				originalStorage,
				originalCollisionStorage,
				checkpointStorage,
				chunkTimeStampRangesStorage,
				chunkWeightStorage);

			return scavengeState;
		}
	}
}
