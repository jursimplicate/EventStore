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

			//qqqq something needs to write a scavengepoint into the log.. is that part of starting
			// a scavenge when there isn't one running? perhaps need to forward the write to the leader,
			// need to use optimistic concurrency so two nodes dont do this concurrently.

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

			//qq not sure, might want to do some sanity checking out here about the checkpoint in
			// relation to the scavengepoint
			if (!_state.TryGetCheckpoint(out var checkpoint))
				checkpoint = null;

			//qq consider exceptions (cancelled, and others)
			//qq old scavenge starts a longrunning task prolly do that
			//qq this would come from the log so that we can stop/resume it.
			//qq need to get the scavenge point for our current checkpoint if we have one
			var scavengePoint = _scavengePointSource.GetScavengePoint();

			if (checkpoint is null || checkpoint is ScavengeCheckpoint.Accumulating)
				Accumulate();
			else if (checkpoint is ScavengeCheckpoint.Calculating<TStreamId>)
				Calculate();
			else if (checkpoint is ScavengeCheckpoint.ExecutingChunks)
				ExecuteChunks();
			else if (checkpoint is ScavengeCheckpoint.ExecutingIndex)
				ExecuteIndex();
			else if (checkpoint is ScavengeCheckpoint.ExecutingChunks)
				ExecuteChunks();
			else
				throw new Exception($"unexpected checkpoint {checkpoint}"); //qq details

			//qq whatever checkpoint we have, if any, we jump to that step and pass it in.
			// calls to subsequent steps don't have that checkpoint. would it be better if each component
			// just managed its own checkpoint and realised it had nothing to do on start
			void Accumulate() {
				_accumulator.Accumulate(
					scavengePoint,
					checkpoint as ScavengeCheckpoint.Accumulating,
					_state,
					cancellationToken);

				Calculate();
			}

			void Calculate() {
				_calculator.Calculate(
					scavengePoint,
					checkpoint as ScavengeCheckpoint.Calculating<TStreamId>,
					_state,
					cancellationToken);

				ExecuteChunks();
			}

			void ExecuteChunks() {
				_chunkExecutor.Execute(
					scavengePoint,
					checkpoint as ScavengeCheckpoint.ExecutingChunks,
					_state,
					cancellationToken);

				ExecuteIndex();
			}


			void ExecuteIndex() {
				_indexExecutor.Execute(
					scavengePoint,
					checkpoint as ScavengeCheckpoint.ExecutingIndex,
					_state,
					scavengerLogger,
					cancellationToken);

				Finish();
			}

			void Finish() {
				//qqq merge phase

				//qqq tidy phase if necessary
				// - could remove certain parts of the scavenge state
				// - what pieces of information can we discard and when should we discard them
				//     - collisions (never)
				//     - hashes (never)
				//     - metastream discardpoints ???
				//     - original stream data
				//     - chunk stamp ranges for empty chunks (probably dont bother)
				//     - chunk weights
				//          - after executing a chunk (chunk executor will do this)

				_state.SetCheckpoint(new ScavengeCheckpoint.Done());
			}
		}
	}
}
