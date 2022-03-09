using System;
using System.Collections.Generic;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.TransactionLog.Scavenging {
	// This datastructure is read and written to by the Accumulator/Calculator/Executors.
	// They contain the scavenge logic, this is just the holder of the data.
	//
	// we store data for metadata streams and for original streams, but we need to store
	// different data for each so we have two maps. we have one collision detector since
	// we need to detect collisions between all of the streams.
	// we don't need to store data for every original stream, only ones that need scavenging.
	public class ScavengeState<TStreamId> : IScavengeState<TStreamId> {

		private readonly CollisionDetector<TStreamId> _collisionDetector;

		// data stored keyed against metadata streams
		private readonly CollisionManager<TStreamId, MetastreamData> _metadatas;

		// data stored keyed against original (non-metadata) streams
		private readonly CollisionManager<TStreamId, DiscardPoint> _originalStreamDatas;

		private readonly ILongHasher<TStreamId> _hasher;
		private readonly IIndexReaderForAccumulator<TStreamId> _indexReaderForAccumulator;

		public ScavengeState(
			ILongHasher<TStreamId> hasher,
			IScavengeMap<TStreamId, Unit> collisionStorage,
			IScavengeMap<ulong, MetastreamData> metaStorage,
			IScavengeMap<TStreamId, MetastreamData> metaCollisionStorage,
			IScavengeMap<ulong, DiscardPoint> originalStorage,
			IScavengeMap<TStreamId, DiscardPoint> originalCollisionStorage,
			IIndexReaderForAccumulator<TStreamId> indexReaderForAccumulator) {

			//qq inject this so that in log v3 we can have a trivial implementation
			// to save us having to look up the stream names repeatedly
			// irl this would be a lru cache.
			var cache = new Dictionary<ulong, TStreamId>();

			//qq inject this so that in log v3 we can have a trivial implementation
			//qq to save us having to look up the stream names repeatedly
			_collisionDetector = new CollisionDetector<TStreamId>(
				HashInUseBefore,
				collisionStorage);

			_hasher = hasher;

			_metadatas = new(
				_hasher,
				_collisionDetector.IsCollision,
				metaStorage,
				metaCollisionStorage);

			_originalStreamDatas = new(
				_hasher,
				_collisionDetector.IsCollision,
				originalStorage,
				originalCollisionStorage);

			_indexReaderForAccumulator = indexReaderForAccumulator;

			bool HashInUseBefore(TStreamId recordStream, long recordPosition, out TStreamId candidateCollidee) {
				var hash = _hasher.Hash(recordStream);

				if (cache.TryGetValue(hash, out candidateCollidee))
					return true;

				//qq look in the index for any record with the current hash up to the limit
				// if any exists then grab the stream name for it
				if (_indexReaderForAccumulator.HashInUseBefore(hash, recordPosition, out candidateCollidee)) {
					cache[hash] = candidateCollidee;
					return true;
				}

				cache[hash] = recordStream;
				candidateCollidee = default;
				return false;
			}
		}








		//
		// STUFF THAT CAME FROM COLLISION MANAGER
		//

		public void DetectCollisions(TStreamId key, long position) {
			var collisionResult = _collisionDetector.DetectCollisions(key, position, out var collision);
			if (collisionResult == CollisionResult.NewCollision) {
				_metadatas.NotifyCollision(collision);
				_originalStreamDatas.NotifyCollision(collision);
			}
		}

		//qq method? property? enumerable? array? clunky allocations at the moment.
		public IEnumerable<TStreamId> Collisions() {
			return _collisionDetector.GetAllCollisions();
		}









		//
		// FOR ACCUMULATOR
		//

		public void NotifyForCollisions(TStreamId streamId, long position) {
			//qq want to make use of the _s?
			_ = _collisionDetector.DetectCollisions(streamId, position, out _);
		}

		public MetastreamData GetMetastreamData(TStreamId streamId) {
			if (!_metadatas.TryGetValue(streamId, out var streamData))
				streamData = MetastreamData.Empty;
			return streamData;
		}

		public void SetMetastreamData(TStreamId streamId, MetastreamData streamData) {
			_metadatas[streamId] = streamData;
		}

		public DiscardPoint GetOriginalStreamData(TStreamId streamId) {
			if (!_originalStreamDatas.TryGetValue(streamId, out var streamData))
				streamData = DiscardPoint.KeepAll;
			return streamData;
		}

		public void SetOriginalStreamData(TStreamId streamId, DiscardPoint streamData) {
			throw new NotImplementedException();
			//qq _originalStreamDatas[streamId] = streamData;
		}





		//
		// FOR CALCULATOR
		//

		// the calculator needs to get the accumulated data for each scavengeable stream
		// it does not have and does not need to know the non colliding stream names.
		//qq consider making this a method?
		public IEnumerable<(StreamHandle<TStreamId>, MetastreamData)> MetastreamDatas =>
			_metadatas.Enumerate();

		public void SetOriginalStreamData(
			StreamHandle<TStreamId> streamHandle,
			DiscardPoint discardPoint) {

			throw new NotImplementedException();
//			_scavengeableStreams[stream] = dp;
		}

		public DiscardPoint GetOriginalStreamData(StreamHandle<TStreamId> streamHandle) {
			throw new NotImplementedException(); //qqqq
		}








		//
		// FOR CHUNK EXECUTOR
		//

		public IEnumerable<IReadOnlyChunkScavengeInstructions<TStreamId>> ChunkInstructionss =>
			throw new NotImplementedException();

		//qq this has to work for both metadata streams and non-metadata streams
		public DiscardPoint GetDiscardPoint(TStreamId streamHandle) {
			throw new NotImplementedException();
		}

		//
		// FOR INDEX EXECUTOR
		//

		//qq this has to work for both metadata streams and non-metadata streams
		public DiscardPoint GetDiscardPoint(StreamHandle<TStreamId> streamHandle) {
			throw new NotImplementedException();
		}

		public bool IsCollision(ulong streamHash) {
			throw new NotImplementedException();
		}
	}
}
