using System;
using System.Collections.Generic;

namespace QuestDB.Change.Tracker.Api
{
    public class WalChangeEventArgs
    {
        public IReadOnlyList<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)> Transactions { get; }
        public DateTime MinTimestamp { get; }
        public DateTime MaxTimestamp { get; }
        public long TotalRows { get; }

        public WalChangeEventArgs(
            IReadOnlyList<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)> txns,
            DateTime minTs,
            DateTime maxTs,
            long totalRows)
        {
            Transactions = txns;
            MinTimestamp = minTs;
            MaxTimestamp = maxTs;
            TotalRows = totalRows;
        }
    }
}