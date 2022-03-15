using System;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class ScavengePointSource : IScavengePointSource {
		private readonly TFChunkDb _db;

		public ScavengePointSource(TFChunkDb db) {
			_db = db;
		}

		//qq wip
		public ScavengePoint GetScavengePoint() {
			return new ScavengePoint {
				Position = _db.Config.ChaserCheckpoint.Read(),
				EffectiveNow = DateTime.Now,
			};
		}
	}
}
