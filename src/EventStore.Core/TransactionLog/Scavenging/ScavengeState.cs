using System.Collections.Generic;
using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Data;

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
		private readonly CollisionMap<TStreamId, DiscardPoint> _metadatastreamDiscardPoints;

		// data stored keyed against original (non-metadata) streams
		private readonly OriginalStreamCollisionMap<TStreamId> _originalStreamDatas;

		private readonly IScavengeMap<int, ChunkTimeStampRange> _chunkTimeStampRanges;
		private readonly IChunkWeightScavengeMap _chunkWeights;
		private readonly IScavengeMap<Unit, ScavengeCheckpoint> _checkpointStorage;

		private readonly ILongHasher<TStreamId> _hasher;
		private readonly IMetastreamLookup<TStreamId> _metastreamLookup;

		public ScavengeState(
			ILongHasher<TStreamId> hasher,
			IMetastreamLookup<TStreamId> metastreamLookup,
			IScavengeMap<TStreamId, Unit> collisionStorage,
			IScavengeMap<ulong, TStreamId> hashes,
			IScavengeMap<ulong, DiscardPoint> metaStorage,
			IScavengeMap<TStreamId, DiscardPoint> metaCollisionStorage,
			IOriginalStreamScavengeMap<ulong> originalStorage,
			IOriginalStreamScavengeMap<TStreamId> originalCollisionStorage,
			//qq wanna key to store multiple checkpoints?
			IScavengeMap<Unit, ScavengeCheckpoint> checkpointStorage,
			IScavengeMap<int, ChunkTimeStampRange> chunkTimeStampRanges,
			IChunkWeightScavengeMap chunkWeights) {


			//qq inject this so that in log v3 we can have a trivial implementation
			//qq to save us having to look up the stream names repeatedly
			_collisionDetector = new CollisionDetector<TStreamId>(
				//qq configurable cacheMaxCount
				new LruCachingScavengeMap<ulong, TStreamId>(hashes, cacheMaxCount: 10_000),
				collisionStorage,
				hasher);

			_hasher = hasher;
			_metastreamLookup = metastreamLookup;
			_checkpointStorage = checkpointStorage;
			_metadatastreamDiscardPoints = new CollisionMap<TStreamId, DiscardPoint>(
				_hasher,
				_collisionDetector.IsCollision,
				metaStorage,
				metaCollisionStorage);

			_originalStreamDatas = new OriginalStreamCollisionMap<TStreamId>(
				_hasher,
				_collisionDetector.IsCollision,
				originalStorage,
				originalCollisionStorage);

			_chunkTimeStampRanges = chunkTimeStampRanges;
			_chunkWeights = chunkWeights;
		}




		public void SetCheckpoint(ScavengeCheckpoint checkpoint) {
			_checkpointStorage[Unit.Instance] = checkpoint;
		}

		//qqqq where to wire in the json conversion
		// and, we probably want to spot corrupt checkpoint and treat it differently to 
		// no checkpoint. perhaps require deleting the whole scavenge state?
		public bool TryGetCheckpoint(out ScavengeCheckpoint checkpoint) =>
			_checkpointStorage.TryGetValue(Unit.Instance, out checkpoint);

		//qqqqqqqq not sure whether we want to use these minimally let the storage
		// wrap everything in little transactions and just use these when necessary
		// or wrap big chunks of things in transactions.
		private void BeginTransaction() {
			throw new NotImplementedException();
		}

		private void CommitTransaction() {
			throw new NotImplementedException();
		}




		//
		// STUFF THAT CAME FROM COLLISION MANAGER
		//

		//qq method? property? enumerable? array? clunky allocations at the moment.
		public IEnumerable<TStreamId> Collisions() {
			return _collisionDetector.GetAllCollisions();
		}

		public bool TryGetOriginalStreamData(
			TStreamId streamId,
			out OriginalStreamData originalStreamData) =>

			_originalStreamDatas.TryGetValue(streamId, out originalStreamData);








		//
		// FOR ACCUMULATOR
		//

		public void DetectCollisions(TStreamId streamId) {
			var collisionResult = _collisionDetector.DetectCollisions(
				streamId,
				out var collision);

			if (collisionResult == CollisionResult.NewCollision) {
				_metadatastreamDiscardPoints.NotifyCollision(collision);
				_originalStreamDatas.NotifyCollision(collision);
			}
		}

		public void SetMetastreamDiscardPoint(TStreamId streamId, DiscardPoint discardPoint) {
			_metadatastreamDiscardPoints[streamId] = discardPoint;
		}

		public void SetOriginalStreamMetadata(TStreamId originalStreamId, StreamMetadata metadata) {
			_originalStreamDatas.SetMetadata(originalStreamId, metadata);
		}

		public void SetOriginalStreamTombstone(TStreamId streamId) {
			_originalStreamDatas.SetTombstone(streamId);
		}

		public void SetChunkTimeStampRange(int logicalChunkNumber, ChunkTimeStampRange range) {
			_chunkTimeStampRanges[logicalChunkNumber] = range;
		}




		//
		// FOR CALCULATOR
		//

		//qq name
		public IEnumerable<(StreamHandle<TStreamId>, OriginalStreamData)> OriginalStreamsToScavenge(
			StreamHandle<TStreamId> checkpoint) {

			return _originalStreamDatas.Enumerate(checkpoint);
		}


		public void SetOriginalStreamDiscardPoints(
			StreamHandle<TStreamId> handle,
			DiscardPoint discardPoint,
			DiscardPoint maybeDiscardPoint) {

			_originalStreamDatas.SetDiscardPoints(handle, discardPoint, maybeDiscardPoint);
		}

		public void IncreaseChunkWeight(int logicalChunkNumber, float extraWeight) {
			_chunkWeights.IncreaseWeight(logicalChunkNumber, extraWeight);
		}

		public bool TryGetChunkTimeStampRange(int logicalChunkNumber, out ChunkTimeStampRange range) =>
			_chunkTimeStampRanges.TryGetValue(logicalChunkNumber, out range);




		//
		// FOR CHUNK EXECUTOR
		//
		public bool TryGetChunkWeight(int chunkNumber, out float weight) =>
			_chunkWeights.TryGetValue(chunkNumber, out weight);

		public void ResetChunkWeight(int chunkNumber) {
			_chunkWeights.TryRemove(chunkNumber, out _);
		}

		public bool TryGetStreamExecutionDetails(
			TStreamId streamId,
			out StreamExecutionDetails details) =>

			_originalStreamDatas.TryGetStreamExecutionDetails(streamId, out details);


		public bool TryGetMetastreamDiscardPoint(TStreamId streamId, out DiscardPoint discardPoint) =>
			_metadatastreamDiscardPoints.TryGetValue(streamId, out discardPoint);

		//
		// FOR INDEX EXECUTOR
		//

		public bool TryGetDiscardPoint(
			StreamHandle<TStreamId> handle,
			out DiscardPoint discardPoint) {

			// here we know that the handle is of the correct kind
			// but we do not know whether it is for a metastream or an originalstream.
			switch (handle.Kind) {
				case StreamHandle.Kind.Hash:
					// not a collision, but we do not know whether it is a metastream or not.
					// check both maps (better if we didnt have to though..)
					return TryGetDiscardPointForOriginalStream(handle, out discardPoint)
						|| TryGetDiscardPointForMetadataStream(handle, out discardPoint);
				case StreamHandle.Kind.Id:
					// collision, but at least we can tell whether it is a metastream or not.
					// so just check one map.
					return _metastreamLookup.IsMetaStream(handle.StreamId)
						? TryGetDiscardPointForMetadataStream(handle, out discardPoint)
						: TryGetDiscardPointForOriginalStream(handle, out discardPoint);
				default:
					throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
			}
		}

		private bool TryGetDiscardPointForMetadataStream(
			StreamHandle<TStreamId> handle,
			out DiscardPoint discardPoint) =>

			_metadatastreamDiscardPoints.TryGetValue(handle, out discardPoint);

		private bool TryGetDiscardPointForOriginalStream(
			StreamHandle<TStreamId> handle,
			out DiscardPoint discardPoint) {

			if (!_originalStreamDatas.TryGetValue(handle, out var streamData)) {
				discardPoint = default;
				return false;
			}

			discardPoint = streamData.DiscardPoint;
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
