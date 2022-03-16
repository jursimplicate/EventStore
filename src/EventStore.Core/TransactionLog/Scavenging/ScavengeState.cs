using System;
using System.Collections.Generic;
using System.Linq;
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
			//qq odd to pass something that is 'foraccumulator' in here
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

		//qq method? property? enumerable? array? clunky allocations at the moment.
		public IEnumerable<TStreamId> Collisions() {
			return _collisionDetector.GetAllCollisions();
		}









		//
		// FOR ACCUMULATOR
		//

		public void NotifyForCollisions(TStreamId streamId, long position) {
			//qq want to make use of the _s?
			var collisionResult = _collisionDetector.DetectCollisions(
				streamId,
				position,
				out var collision);

			if (collisionResult == CollisionResult.NewCollision) {
				_metadatas.NotifyCollision(collision);
				_originalStreamDatas.NotifyCollision(collision);
			}
		}

		public bool TryGetMetastreamData(TStreamId streamId, out MetastreamData streamData) =>
			_metadatas.TryGetValue(streamId, out streamData);
	
		public void SetMetastreamData(TStreamId streamId, MetastreamData streamData) {
			_metadatas[streamId] = streamData;
		}

		public void SetOriginalStreamData(TStreamId streamId, DiscardPoint streamData) {
			_originalStreamDatas[streamId] = streamData;
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

			_originalStreamDatas[streamHandle] = discardPoint;
		}

		public bool TryGetOriginalStreamData(
			StreamHandle<TStreamId> streamHandle,
			out DiscardPoint discardPoint) =>

			_originalStreamDatas.TryGetValue(streamHandle, out discardPoint);

		public bool TryGetHeuristic(int chunkNumber, out long heuristic) =>
			_chunkWeights.TryGetValue(chunkNumber, out heuristic);

		public void SetHeuristic(int chunkNumber, long heuristic) {
			_chunkWeights[chunkNumber] = heuristic;
		}


		//qqqq inject, maybe even use IScavengeMap interface for now.
		private readonly Dictionary<int, long> _chunkWeights = new();



		//
		// FOR CHUNK EXECUTOR
		//

		public IEnumerable<ChunkHeuristic> GetChunkHeuristics(ScavengePoint scavengePoint) =>
			_chunkWeights.Select(x => new ChunkHeuristic {
				ChunkNumber = x.Key,
				Weight = x.Value,
			});

		//qq this has to work for both metadata streams and non-metadata streams
		public bool TryGetDiscardPoint(TStreamId streamHandle, out DiscardPoint discardPoint) {
			throw new NotImplementedException();
		}

		public bool OnChunkScavenged(int chunkNumber) {
			throw new NotImplementedException();
		}

		//
		// FOR INDEX EXECUTOR
		//

		public bool TryGetDiscardPoint(
			StreamHandle<TStreamId> streamHandle,
			out DiscardPoint discardPoint) {

			//qq here we know that the streamHandle is of the correct kind, so if it is a hash handle
			// then we know the hash does not collide, but we do not know whether it is for a 
			// stream or a metastream. but since we know it does not collide we can just check
			// both maps (better if we didnt have to though..)
			//if (streamHandle.IsHash) {
			//	if (_metadatas.TryGetValue(streamHandle, out var metastreamData)) {
			//		return metastreamData.DiscardPoint.Value;
			//	}

			//} else {
			//	//qqqq temp. it might be null, it might not be a metadata stream
			//	return _metadatas[streamHandle.StreamId].DiscardPoint.Value;
			//}

			discardPoint = DiscardPoint.KeepAll;
			return true;
		}

		public bool IsCollision(ulong streamHash) {
			//qq track these as we go rather than calculating each time on demand.
			var collidingHashes = new HashSet<ulong>();
			
			foreach (var collidingKey in _collisionDetector.GetAllCollisions()) {
				collidingHashes.Add(_hasher.Hash(collidingKey));
			}

			return collidingHashes.Contains(streamHash);
		}
	}
}
