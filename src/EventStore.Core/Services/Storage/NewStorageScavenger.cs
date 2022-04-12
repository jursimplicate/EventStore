using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.Services.Storage {
	//qq name
	public abstract class NewStorageScavenger {
	}

	//qq consider refactoring with StorageScavenger once this is more fleshed out
	//qq name, especailly wrt the wrapped scanvenger. perhaps this is the scavenger service.
	public class NewStorageScavenger<TStreamId> :
		NewStorageScavenger,
		IHandle<SystemMessage.StateChangeMessage>,
		IHandle<ClientMessage.ScavengeDatabase>,
		IHandle<ClientMessage.StopDatabaseScavenge> {

		private readonly IScavenger _scavenger;
		private readonly ITFChunkScavengerLogManager _logManager;

		private CancellationTokenSource _cancellationTokenSource;

		public NewStorageScavenger(
			ITFChunkScavengerLogManager logManager,
			IScavenger scavenger) {

			Ensure.NotNull(scavenger, nameof(scavenger));
			Ensure.NotNull(logManager, nameof(logManager));

			_scavenger = scavenger;
			_logManager = logManager;
		}

		public void Handle(SystemMessage.StateChangeMessage message) {
			if (message.State == VNodeState.Master || message.State == VNodeState.Slave) {
				_logManager.Initialise();
			}
		}

		public void Handle(ClientMessage.ScavengeDatabase message) {
			//qq if here is the right place to do so, check if scavenge is already running etc
			_cancellationTokenSource = new CancellationTokenSource();
			_ = _scavenger.RunAsync(
				_logManager.CreateLog(),
				_cancellationTokenSource.Token);
		}

		public void Handle(ClientMessage.StopDatabaseScavenge message) {
			//qq check it is for the right scavenge etc (see StorageScavenger.cs)
			// if here is the right place to do so
			_cancellationTokenSource.Cancel();
		}
	}
}
