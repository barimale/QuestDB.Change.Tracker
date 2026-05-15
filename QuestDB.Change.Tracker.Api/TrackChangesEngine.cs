using Npgsql;
using QuestDB.Change.Tracker.Api.Extensions;
using QuestDB.Change.Tracker.Api.Model;
using QuestDB.Change.Tracker.Api.Model.Connection;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace QuestDB.Change.Tracker.Api
{
    /// <summary>
    /// Engine for tracking changes in QuestDB tables using WAL (Write-Ahead Log) transactions.
    /// Supports dependency injection for testability.
    /// </summary>
    public class TrackChangesEngine
    {
        private readonly SynchronizationContext _ui;
        private readonly IDbConnectionFactory _connectionFactory;

        public event Func<WalChangeEventArgs, Task>? OnChange;

        /// <summary>
        /// Initializes a new instance of the TrackChangesEngine class.
        /// </summary>
        /// <param name="connectionFactory">Factory for creating database connections. If null, legacy mode is used.</param>
        public TrackChangesEngine(IDbConnectionFactory? connectionFactory = null)
        {
            _ui = SynchronizationContext.Current!;
            _connectionFactory = connectionFactory!;
        }

        /// <summary>
        /// Tracks changes in a table and raises events when transactions meet the row threshold.
        /// </summary>
        /// <param name="tableName">Name of the table to track.</param>
        /// <param name="columns">Comma-separated list of columns to aggregate.</param>
        /// <param name="rowThreshold">Minimum number of rows to trigger a change event.</param>
        /// <param name="checkIntervalInSeconds">Interval in seconds between checks for new transactions.</param>
        /// <param name="timestampColumn">Name of the timestamp column for filtering.</param>
        /// <param name="trackingTable">Name of the tracking table to store progress (optional).</param>
        /// <param name="trackingId">Unique ID for this tracking session (optional).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task TrackAsync(
             string tableName,
             string columns,
             int rowThreshold,
             int checkIntervalInSeconds,
             string timestampColumn,
             string trackingTable,
             string trackingId,
             CancellationToken ct)
        {
            await using var conn = (NpgsqlConnection)await _connectionFactory.CreateConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();

            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            // Initialize tracking: setup tracking table and get latest transaction IDs
            var (latestTxnId, latestStructureVersion) = await InitializeTrackingAsync(
                cmd, tableName, trackingTable, trackingId, ct);

            Console.WriteLine($"Starting from transaction ID: {latestTxnId} with structure version: {latestStructureVersion}");

            // Main tracking loop
            await TrackingLoopAsync(
                cmd,
                tableName,
                columns,
                timestampColumn,
                trackingTable,
                trackingId,
                rowThreshold,
                checkIntervalInSeconds,
                latestTxnId,
                latestStructureVersion,
                ct);

            Console.WriteLine("TrackAsync stopped.");
        }

        /// <summary>
        /// Initializes tracking by setting up the tracking table and retrieving the latest transaction IDs.
        /// </summary>
        private async Task<(long latestTxnId, long latestStructureVersion)> InitializeTrackingAsync(
            NpgsqlCommand cmd,
            string tableName,
            string trackingTable,
            string trackingId,
            CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(trackingTable) && !string.IsNullOrEmpty(trackingId))
            {
                return await InitializeWithTrackingTableAsync(cmd, tableName, trackingTable, trackingId, ct);
            }

            return await GetLatestTransactionIdsAsync(cmd, tableName, ct);
        }

        /// <summary>
        /// Initializes tracking with a dedicated tracking table to persist progress.
        /// </summary>
        private async Task<(long latestTxnId, long latestStructureVersion)> InitializeWithTrackingTableAsync(
            NpgsqlCommand cmd,
            string tableName,
            string trackingTable,
            string trackingId,
            CancellationToken ct)
        {
            // Create tracking table if it doesn't exist
            await CreateTrackingTableAsync(cmd, trackingTable, ct);

            // Try to retrieve last known transaction ID from tracking table
            var foundTxn = await GetLastTrackedTransactionAsync(cmd, trackingTable, trackingId, ct);

            if (foundTxn.HasValue)
            {
                var structureVersion = await GetStructureVersionAsync(cmd, tableName, foundTxn.Value, ct);
                return (foundTxn.Value, structureVersion);
            }

            // No previous tracking found, get latest from WAL
            return await GetLatestTransactionIdsAsync(cmd, tableName, ct);
        }

        /// <summary>
        /// Creates the tracking table in the database.
        /// </summary>
        private async Task CreateTrackingTableAsync(
            NpgsqlCommand cmd,
            string trackingTable,
            CancellationToken ct)
        {
            cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {trackingTable} (
                timestamp TIMESTAMP,
                trackingId SYMBOL,
                tableName SYMBOL,
                sequencerTxn LONG
            ) timestamp (timestamp) PARTITION BY DAY WAL DEDUP UPSERT KEYS(timestamp, trackingId, tableName);";

            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Retrieves the last tracked transaction ID from the tracking table.
        /// </summary>
        private async Task<long?> GetLastTrackedTransactionAsync(
            NpgsqlCommand cmd,
            string trackingTable,
            string trackingId,
            CancellationToken ct)
        {
            cmd.CommandText = $@"
            SELECT tableName, sequencerTxn
            FROM {trackingTable}
            WHERE trackingId = '{trackingId}'
            LATEST ON timestamp
            PARTITION BY tableName;";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return reader.GetInt64(1);
            }

            return null;
        }

        /// <summary>
        /// Retrieves the structure version for a specific transaction.
        /// </summary>
        private async Task<long> GetStructureVersionAsync(
            NpgsqlCommand cmd,
            string tableName,
            long txnId,
            CancellationToken ct)
        {
            var result = await cmd.ExecuteScalarFromQueryAsync(
                $"SELECT structureVersion FROM wal_transactions('{tableName}') WHERE sequencerTxn={txnId} LIMIT 1",
                ct);

            return (long)result;
        }

        /// <summary>
        /// Retrieves the latest transaction ID and structure version from WAL transactions.
        /// </summary>
        private async Task<(long latestTxnId, long latestStructureVersion)> GetLatestTransactionIdsAsync(
            NpgsqlCommand cmd,
            string tableName,
            CancellationToken ct)
        {
            await using var reader = await cmd.ExecuteReaderFromQueryAsync(
                $"SELECT sequencerTxn, structureVersion FROM wal_transactions('{tableName}') ORDER BY sequencerTxn DESC LIMIT 1",
                ct);

            await reader.ReadAsync(ct);
            return (reader.GetInt64(0), reader.GetInt64(1));
        }

        /// <summary>
        /// Main tracking loop that continuously monitors for changes and triggers events.
        /// </summary>
        private async Task TrackingLoopAsync(
            NpgsqlCommand cmd,
            string tableName,
            string columns,
            string timestampColumn,
            string trackingTable,
            string trackingId,
            int rowThreshold,
            int checkIntervalInSeconds,
            long initialLatestTxnId,
            long initialLatestStructureVersion,
            CancellationToken ct)
        {
            long latestTxnId = initialLatestTxnId;
            long latestStructureVersion = initialLatestStructureVersion;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(checkIntervalInSeconds * 1000, ct);

                // Fetch new transactions
                var newTxns = await FetchNewTransactionsAsync(cmd, tableName, latestTxnId, ct);

                if (newTxns.Count == 0)
                    continue;

                // Update structure version if changed
                latestStructureVersion = UpdateStructureVersion(newTxns, latestStructureVersion);

                // Calculate total rows and check threshold
                long totalRows = CalculateTotalRows(newTxns);
                if (totalRows < rowThreshold)
                    continue;

                // Get min/max timestamps
                var (minTs, maxTs) = GetTimestampRange(newTxns);
                if (minTs == null || maxTs == null)
                    continue;

                // Execute aggregation query
                await ExecuteAggregationAsync(cmd, tableName, columns, timestampColumn, minTs.Value, maxTs.Value, ct);

                // Update latest transaction ID
                latestTxnId = newTxns[^1].txn;

                // Save progress to tracking table if configured
                if (!string.IsNullOrEmpty(trackingTable) && !string.IsNullOrEmpty(trackingId))
                {
                    await SaveProgressAsync(cmd, trackingTable, trackingId, tableName, latestTxnId, ct);
                }

                // Fire change event
                await FireChangeEventAsync(newTxns, minTs.Value, maxTs.Value, totalRows);
            }
        }

        /// <summary>
        /// Fetches new transactions from WAL that occurred after the given transaction ID.
        /// </summary>
        private async Task<List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)>> FetchNewTransactionsAsync(
            NpgsqlCommand cmd,
            string tableName,
            long afterTxnId,
            CancellationToken ct)
        {
            cmd.CommandText = $@"
            SELECT sequencerTxn, minTimestamp, maxTimestamp, rowCount, structureVersion
            FROM wal_transactions('{tableName}')
            WHERE sequencerTxn > {afterTxnId}";

            var newTxns = new List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)>();

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                newTxns.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.GetInt64(4)
                ));
            }

            await reader.CloseAsync();
            return newTxns;
        }

        /// <summary>
        /// Updates structure version if it changed in any of the new transactions.
        /// </summary>
        private long UpdateStructureVersion(
            List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)> newTxns,
            long currentStructureVersion)
        {
            long latestVersion = currentStructureVersion;

            foreach (var txn in newTxns)
            {
                if (txn.structVer != latestVersion)
                {
                    Console.WriteLine($"Structure version changed from {latestVersion} to {txn.structVer} on transaction {txn.txn}");
                    latestVersion = txn.structVer;
                }
            }

            return latestVersion;
        }

        /// <summary>
        /// Calculates total rows from all transactions.
        /// </summary>
        private long CalculateTotalRows(List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)> txns)
        {
            long totalRows = 0;
            foreach (var txn in txns)
            {
                if (txn.rows.HasValue)
                    totalRows += txn.rows.Value;
            }

            return totalRows;
        }

        /// <summary>
        /// Determines the minimum and maximum timestamps across all transactions.
        /// </summary>
        private (DateTime? minTs, DateTime? maxTs) GetTimestampRange(
            List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)> txns)
        {
            DateTime? minTs = null;
            DateTime? maxTs = null;

            foreach (var txn in txns)
            {
                if (txn.minTs.HasValue)
                    minTs = minTs == null ? txn.minTs : (txn.minTs < minTs ? txn.minTs : minTs);

                if (txn.maxTs.HasValue)
                    maxTs = maxTs == null ? txn.maxTs : (txn.maxTs > maxTs ? txn.maxTs : maxTs);
            }

            return (minTs, maxTs);
        }

        /// <summary>
        /// Executes the aggregation query and prints results to console.
        /// </summary>
        private async Task ExecuteAggregationAsync(
            NpgsqlCommand cmd,
            string tableName,
            string columns,
            string timestampColumn,
            DateTime minTs,
            DateTime maxTs,
            CancellationToken ct)
        {
            var aggParts = BuildAggregationParts(columns);
            var aggQuery = BuildAggregationQuery(tableName, aggParts, timestampColumn, minTs, maxTs);

            cmd.CommandText = aggQuery;

            await using var result = await cmd.ExecuteReaderAsync(ct);
            await result.ReadAsync(ct);

            PrintAggregationResults(columns, result);
            await result.CloseAsync();
        }

        /// <summary>
        /// Builds the list of aggregation parts (first, last, min, max, avg) for each column.
        /// </summary>
        private List<string> BuildAggregationParts(string columns)
        {
            var columnList = columns.Split(',');
            var aggParts = new List<string>();

            foreach (var col in columnList)
            {
                aggParts.Add($"first({col}) AS {col}_first");
                aggParts.Add($"last({col}) AS {col}_last");
                aggParts.Add($"min({col}) AS {col}_min");
                aggParts.Add($"max({col}) AS {col}_max");
                aggParts.Add($"avg({col}) AS {col}_avg");
            }

            return aggParts;
        }

        /// <summary>
        /// Constructs the aggregation SQL query.
        /// </summary>
        private string BuildAggregationQuery(
            string tableName,
            List<string> aggParts,
            string timestampColumn,
            DateTime minTs,
            DateTime maxTs)
        {
            return $@"
            SELECT {string.Join(", ", aggParts)}
            FROM {tableName}
            WHERE {timestampColumn} BETWEEN '{minTs:yyyy-MM-ddTHH:mm:ss}' AND '{maxTs:yyyy-MM-ddTHH:mm:ss}'";
        }

        /// <summary>
        /// Prints aggregation results to the console.
        /// </summary>
        private void PrintAggregationResults(
            string columns,
            IDataReader result)
        {
            var columnList = columns.Split(',');
            var headers = new List<string>();

            foreach (var col in columnList)
            {
                headers.Add($"{col}_first");
                headers.Add($"{col}_last");
                headers.Add($"{col}_min");
                headers.Add($"{col}_max");
                headers.Add($"{col}_avg");
            }

            Console.WriteLine(string.Join(", ", headers));

            var values = new object[headers.Count];
            result.GetValues(values);
            Console.WriteLine(string.Join(", ", values));
        }

        /// <summary>
        /// Saves the current progress (latest transaction ID) to the tracking table.
        /// </summary>
        private async Task SaveProgressAsync(
            NpgsqlCommand cmd,
            string trackingTable,
            string trackingId,
            string tableName,
            long latestTxnId,
            CancellationToken ct)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            cmd.CommandText = $@"
                INSERT INTO {trackingTable} (timestamp, trackingId, tableName, sequencerTxn)
                VALUES ('{now}', '{trackingId}', '{tableName}', {latestTxnId})";

            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Fires the OnChange event if subscribers are registered.
        /// </summary>
        private async Task FireChangeEventAsync(
            List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)> newTxns,
            DateTime minTs,
            DateTime maxTs,
            long totalRows)
        {
            if (OnChange == null)
                return;

            var changeArgs = new WalChangeEventArgs(newTxns, minTs, maxTs, totalRows);

            if (_ui != null)
            {
                _ui.Post(async _ => await OnChange(changeArgs), null);
            }
            else
            {
                await OnChange(changeArgs);
            }
        }

    }
}
