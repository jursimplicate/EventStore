
namespace EventStore.Core.TransactionLog.LogRecords {
	public class SkippedLogRecord : LogRecord {
		public static readonly SkippedLogRecord Instance = new SkippedLogRecord();

		private SkippedLogRecord() : base(LogRecordType.Skipped, 0, 0)
		{
		}
	}
}
