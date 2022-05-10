using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite {
	public class SqliteScavengeBackend<TStreamId> : ITransactionBackend, IDisposable {
		private const string DbFileName = "scavenging.db";
		private const string ExpectedJournalMode = "wal";
		private const int ExpectedSynchronousValue = 1; // Normal
		private SqliteConnection _connection;
		private SqliteTransaction _transaction;

		public IScavengeMap<TStreamId, Unit> CollisionStorage { get; private set; }
		public IScavengeMap<ulong,TStreamId> Hashes { get; private set; }
		public IScavengeMap<ulong,DiscardPoint> MetaStorage { get; private set; }
		public IScavengeMap<TStreamId,DiscardPoint> MetaCollisionStorage { get; private set; }
		public IOriginalStreamScavengeMap<ulong> OriginalStorage { get; private set; }
		public IOriginalStreamScavengeMap<TStreamId> OriginalCollisionStorage { get; private set; }
		public IScavengeMap<Unit,ScavengeCheckpoint> CheckpointStorage { get; private set; }
		public IScavengeMap<int,ChunkTimeStampRange> ChunkTimeStampRanges { get; private set; }
		public IChunkWeightScavengeMap ChunkWeights { get; private set; }
		private AbstractSqliteBase[] AllMaps { get; set; }

		public SqliteScavengeBackend() {

		}

		public void Initialize(string dir = ".") {
			OpenDbConnection(dir);
			ConfigureFeatures();

			var collisionStorage = new SqliteFixedStructScavengeMap<TStreamId, Unit>("CollisionStorageMap", _connection);
			CollisionStorage = collisionStorage;

			var hashes = new SqliteScavengeMap<ulong, TStreamId>("HashesMap", _connection);
			Hashes = hashes;

			var metaStorage = new SqliteFixedStructScavengeMap<ulong, DiscardPoint>("MetaStorageMap", _connection);
			MetaStorage = metaStorage;
			
			var metaCollisionStorage = new SqliteFixedStructScavengeMap<TStreamId, DiscardPoint>("MetaCollisionMap", _connection);
			MetaCollisionStorage = metaCollisionStorage;
			
			var originalStorage = new SqliteOriginalStreamScavengeMap<ulong>("OriginalStreamStorageMap", _connection);
			OriginalStorage = originalStorage;
			
			var originalCollisionStorage = new SqliteOriginalStreamScavengeMap<TStreamId>("OriginalStreamCollisionStorageMap", _connection);
			OriginalCollisionStorage = originalCollisionStorage;
			
			var checkpointStorage = new SqliteScavengeCheckpointMap<TStreamId>(_connection);
			CheckpointStorage = checkpointStorage;
			
			var chunkTimeStampRanges = new SqliteFixedStructScavengeMap<int, ChunkTimeStampRange>("ChunkTimeStampRangeMap", _connection);
			ChunkTimeStampRanges = chunkTimeStampRanges;
			
			var chunkWeights = new SqliteChunkWeightScavengeMap(_connection);
			ChunkWeights = chunkWeights;

			AllMaps = new AbstractSqliteBase[] { collisionStorage, hashes, metaStorage, metaCollisionStorage,
				originalStorage, originalCollisionStorage, checkpointStorage, chunkTimeStampRanges, chunkWeights };

			Begin();
			
			foreach (var map in AllMaps) {
				map.Initialize();
			}
			
			Commit();
		}

		private void OpenDbConnection(string dir)
		{
			Directory.CreateDirectory(dir);

			var connectionStringBuilder = new SqliteConnectionStringBuilder();
			connectionStringBuilder.DataSource = Path.Combine(dir, DbFileName);
			_connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
			_connection.Open();
		}

		private void ConfigureFeatures() {
			var cmd = _connection.CreateCommand();
			cmd.CommandText = $"PRAGMA journal_mode={ExpectedJournalMode}";
			cmd.ExecuteNonQuery();

			cmd.CommandText = "SELECT * FROM pragma_journal_mode()";
			var journalMode = cmd.ExecuteScalar();
			if (journalMode == null || journalMode.ToString().ToLower() != ExpectedJournalMode) {
				throw new Exception($"SQLite database is in unexpected journal mode: {journalMode}");
			}
			
			cmd.CommandText = $"PRAGMA synchronous={ExpectedSynchronousValue}";
			cmd.ExecuteNonQuery();
			
			cmd.CommandText = "SELECT * FROM pragma_synchronous()";
			var synchronousMode = (long?)cmd.ExecuteScalar();
			if (!synchronousMode.HasValue || synchronousMode.Value != ExpectedSynchronousValue) {
				throw new Exception($"SQLite database is in unexpected synchronous mode: {synchronousMode}");
			}
		}

		public void Begin() {
			if (_connection == null) {
				throw new InvalidOperationException("Cannot start a scavenge state transaction without an open connection");
			}

			if (_transaction != null) {
				throw new InvalidOperationException("Cannot start another scavenge state transaction");
			}
			
			_transaction = _connection.BeginTransaction();
		}

		public void Rollback() {
			if (_transaction == null) {
				throw new InvalidOperationException("Cannot rollback a scavenge state transaction without an active transaction");
			}
			
			_transaction.Rollback();
			_transaction.Dispose();
			_transaction = null;
		}

		public void Commit() {
			if (_transaction == null) {
				throw new InvalidOperationException("Cannot commit a scavenge state transaction without an active transaction");
			}
			
			_transaction.Commit();
			_transaction.Dispose();
			_transaction = null;
		}

		public void Dispose()
		{
			_connection?.Dispose();
			_transaction?.Dispose();
		}
	}
}
