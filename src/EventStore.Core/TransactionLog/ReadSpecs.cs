using System;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog {
	public delegate bool IncludePrepareData(ReadOnlySpan<byte> streamId, long eventNumber);
	public delegate bool IncludePrepareMetadata(ReadOnlySpan<byte> streamId, long eventNumber);
	public delegate BasicPrepareLogRecord BasicPrepareRecordFactory(int size);

	public readonly struct ReadSpecs {
		public bool SkipSystemRecords { get; }
		public bool SkipCommitRecords { get; }
		public bool ReadBasicPrepareRecords { get; }
		public IncludePrepareData IncludePrepareData { get; }
		public IncludePrepareMetadata IncludePrepareMetadata { get; }
		public BasicPrepareRecordFactory BasicPrepareRecordFactory { get; }

		public static ReadSpecs Default = new ReadSpecs(false, false, false, null, null);

		public ReadSpecs(
			bool skipSystemRecords = false,
			bool skipCommitRecords = false,
			bool readBasicPrepareRecords = false,
			IncludePrepareData includePrepareData = null,
			IncludePrepareMetadata includePrepareMetadata = null,
			BasicPrepareRecordFactory basicPrepareRecordFactory = null) {
			SkipSystemRecords = skipSystemRecords;
			SkipCommitRecords = skipCommitRecords;
			ReadBasicPrepareRecords = readBasicPrepareRecords;
			IncludePrepareData = includePrepareData;
			IncludePrepareMetadata = includePrepareMetadata;
			BasicPrepareRecordFactory = basicPrepareRecordFactory;
		}
	}
}
