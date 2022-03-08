using System;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Scavenger<TStreamId> : IScavenger {
		private readonly IScavengeState<TStreamId> _scavengeState;
		private readonly IAccumulator<TStreamId> _accumulator;
		private readonly ICalculator<TStreamId> _calculator;
		private readonly IChunkExecutor<TStreamId> _chunkExecutor;
		private readonly IIndexExecutor<TStreamId> _indexExecutor;
		private readonly TFChunkDb _db;

		public Scavenger(
			IScavengeState<TStreamId> scavengeState,
			//qq might need to be factories if we need to instantiate new when calling start()
			IAccumulator<TStreamId> accumulator,
			ICalculator<TStreamId> calculator,
			IChunkExecutor<TStreamId> chunkExecutor,
			IIndexExecutor<TStreamId> indexExecutor,
			TFChunkDb db) {

			_scavengeState = scavengeState;
			_accumulator = accumulator;
			_calculator = calculator;
			_chunkExecutor = chunkExecutor;
			_indexExecutor = indexExecutor;
			_db = db;
		}

		public void Start() {
			//qq this would come from the log so that we can stop/resume it.
			//qq implement stopping and resuming. at each stage. cts?
			var scavengePoint = new ScavengePoint {
				Position = _db.Config.ChaserCheckpoint.Read(),
				EffectiveNow = DateTime.Now,
			};

			_accumulator.Accumulate(scavengePoint, _scavengeState);
			_calculator.Calculate(scavengePoint, _scavengeState);
			_chunkExecutor.Execute(_scavengeState);
			_indexExecutor.Execute(_scavengeState);
			//qqqq tidy.. maybe call accumulator.done or something?
		}

		public void Stop() {
			throw new NotImplementedException(); //qq
		}
	}
}
