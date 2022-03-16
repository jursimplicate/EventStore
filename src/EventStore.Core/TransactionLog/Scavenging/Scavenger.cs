using System;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Scavenger<TStreamId> : IScavenger {
		private readonly IScavengeState<TStreamId> _scavengeState;
		private readonly IAccumulator<TStreamId> _accumulator;
		private readonly ICalculator<TStreamId> _calculator;
		private readonly IChunkExecutor<TStreamId> _chunkExecutor;
		private readonly IIndexExecutor<TStreamId> _indexExecutor;
		private readonly IScavengePointSource _scavengePointSource;

		public Scavenger(
			IScavengeState<TStreamId> scavengeState,
			//qq might need to be factories if we need to instantiate new when calling start()
			IAccumulator<TStreamId> accumulator,
			ICalculator<TStreamId> calculator,
			IChunkExecutor<TStreamId> chunkExecutor,
			IIndexExecutor<TStreamId> indexExecutor,
			IScavengePointSource scavengePointSource) {

			_scavengeState = scavengeState;
			_accumulator = accumulator;
			_calculator = calculator;
			_chunkExecutor = chunkExecutor;
			_indexExecutor = indexExecutor;
			_scavengePointSource = scavengePointSource;
		}

		public void Start() {
			//qq implement stopping and resuming at each stage, so that it picks up
			// where it left off. cts?
			// for now this starts from the beginning

			//qq this would come from the log so that we can stop/resume it.
			var scavengePoint = _scavengePointSource.GetScavengePoint();

			_accumulator.Accumulate(scavengePoint, _scavengeState);
			_calculator.Calculate(scavengePoint, _scavengeState);
			_chunkExecutor.Execute(scavengePoint, _scavengeState);
			_indexExecutor.Execute(_scavengeState);
			//qqqq tidy.. maybe call accumulator.done or something?
		}

		public void Stop() {
			throw new NotImplementedException(); //qq
		}
	}
}
