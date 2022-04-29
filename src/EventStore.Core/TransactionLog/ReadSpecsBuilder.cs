using System;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog {
	public class ReadSpecsBuilder {
		private bool _skipSystemRecords;
		private bool _skipCommitRecords;
		private bool _readBasicPrepareRecords;
		private IncludePrepareData _includePrepareData;
		private IncludePrepareMetadata _includePrepareMetadata;
		private BasicPrepareRecordFactory _basicPrepareRecordFactory;

		public ReadSpecsBuilder ReadBasicPrepareRecords(
			IncludePrepareData includeData,
			IncludePrepareMetadata includeMetadata,
			BasicPrepareRecordFactory basicPrepareRecordFactory) {
			_readBasicPrepareRecords = true;
			_includePrepareData = includeData;
			_includePrepareMetadata = includeMetadata;
			_basicPrepareRecordFactory = basicPrepareRecordFactory;
			return this;
		}

		public ReadSpecsBuilder SkipSystemRecords() {
			_skipSystemRecords = true;
			return this;
		}

		public ReadSpecsBuilder SkipCommitRecords() {
			_skipCommitRecords = true;
			return this;
		}

		public static implicit operator ReadSpecs(ReadSpecsBuilder builder) {
			return builder.Build();
		}

		public ReadSpecs Build() {
			return new ReadSpecs(
				_skipSystemRecords,
				_skipCommitRecords,
				_readBasicPrepareRecords,
				_includePrepareData,
				_includePrepareMetadata,
				_basicPrepareRecordFactory);
		}
	}
}
