using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Log;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Scavenger {
		protected static readonly ILogger Log = LogManager.GetLoggerFor<Scavenger>();
	}

	public class Scavenger<TStreamId> : Scavenger, IScavenger {
		private readonly IScavengeState<TStreamId> _state;
		private readonly IAccumulator<TStreamId> _accumulator;
		private readonly ICalculator<TStreamId> _calculator;
		private readonly IChunkExecutor<TStreamId> _chunkExecutor;
		private readonly IIndexExecutor<TStreamId> _indexExecutor;
		private readonly IScavengePointSource _scavengePointSource;

		public Scavenger(
			IScavengeState<TStreamId> state,
			//qq might need to be factories if we need to instantiate new when calling start()
			IAccumulator<TStreamId> accumulator,
			ICalculator<TStreamId> calculator,
			IChunkExecutor<TStreamId> chunkExecutor,
			IIndexExecutor<TStreamId> indexExecutor,
			IScavengePointSource scavengePointSource) {

			_state = state;
			_accumulator = accumulator;
			_calculator = calculator;
			_chunkExecutor = chunkExecutor;
			_indexExecutor = indexExecutor;
			_scavengePointSource = scavengePointSource;
		}

		public Task RunAsync(
			ITFChunkScavengerLog scavengerLogger,
			CancellationToken cancellationToken) {

			//qq similar to TFChunkScavenger.Scavenge. consider what we want to log exactly
			return Task.Factory.StartNew(() => {
				var sw = Stopwatch.StartNew();

				ScavengeResult result = ScavengeResult.Success;
				string error = null;
				try {
					scavengerLogger.ScavengeStarted();

					StartInternal(scavengerLogger, cancellationToken);

				} catch (OperationCanceledException) {
					Log.Info("SCAVENGING: Scavenge cancelled.");
					result = ScavengeResult.Stopped;
				} catch (Exception exc) {
					result = ScavengeResult.Failed;
					Log.ErrorException(exc, "SCAVENGING: error while scavenging DB.");
					error = string.Format("Error while scavenging DB: {0}.", exc.Message);
					throw; //qqq probably dont want this line, just here for tests i dont want to touch atm
				} finally {
					try {
						scavengerLogger.ScavengeCompleted(result, error, sw.Elapsed);
					} catch (Exception ex) {
						Log.ErrorException(
							ex,
							"Error whilst recording scavenge completed. " +
							"Scavenge result: {result}, Elapsed: {elapsed}, Original error: {e}",
							result, sw.Elapsed, error);
					}
				}
			}, TaskCreationOptions.LongRunning);
		}

		private void StartInternal(
			ITFChunkScavengerLog scavengerLogger,
			CancellationToken cancellationToken) {

			//qq consider exceptions (cancelled, and others)
			//qq old scavenge starts a longrunning task prolly do that
			//qq this would come from the log so that we can stop/resume it.
			var scavengePoint = _scavengePointSource.GetScavengePoint();

			//qq not sure, might want to do some sanity checking out here about the checkpoint in
			// relation to the scavengepoint
			if (!_state.TryGetCheckpoint(out var checkpoint))
				checkpoint = null;

			//qq whatever checkpoint we have, if any, we jump to that step and pass it in.
			// calls to subsequent steps don't have that checkpoint. would it be better if each component
			// just managed its own checkpoint and realised it had nothing to do on start
			_accumulator.Accumulate(
				scavengePoint,
				checkpoint as ScavengeCheckpoint.Accumulating,
				_state,
				cancellationToken);

			_calculator.Calculate(
				scavengePoint,
				checkpoint as ScavengeCheckpoint.Calculating<TStreamId>,
				_state,
				cancellationToken);

			_chunkExecutor.Execute(
				scavengePoint,
				checkpoint as ScavengeCheckpoint.ExecutingChunks,
				_state,
				cancellationToken);

			_indexExecutor.Execute(
				scavengePoint,
				checkpoint as ScavengeCheckpoint.ExecutingIndex,
				_state,
				scavengerLogger,
				cancellationToken);

			//qqq merge phase
			//qqq tidy phase
		}
	}
}
