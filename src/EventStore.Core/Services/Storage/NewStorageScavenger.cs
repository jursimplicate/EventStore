using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.Services.Storage {
	//qq name
	public abstract class NewStorageScavenger {
	}

	//qq name, especailly wrt the wrapped scanvenger. perhaps this is the scavenger service.
	public class NewStorageScavenger<TStreamId> :
		NewStorageScavenger,
		IHandle<SystemMessage.StateChangeMessage>,
		IHandle<ClientMessage.ScavengeDatabase>,
		IHandle<ClientMessage.StopDatabaseScavenge> {

		private readonly IScavenger _scavenger;

		public NewStorageScavenger(IScavenger scavenger) {
			_scavenger = scavenger;
		}

		public void Handle(SystemMessage.StateChangeMessage message) {
			//qq anything to do?
		}

		public void Handle(ClientMessage.ScavengeDatabase message) {
			_scavenger.Start();
		}

		public void Handle(ClientMessage.StopDatabaseScavenge message) {
			_scavenger.Stop();
		}
	}
}
