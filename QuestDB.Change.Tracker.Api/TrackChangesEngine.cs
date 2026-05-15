using Npgsql;
using QuestDB.Change.Tracker.Api.Extensions;
using QuestDB.Change.Tracker.Api.Model;
using QuestDB.Change.Tracker.Api.Model.Connection;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
        /// 
        /// NOTE: This overload is deprecated. Use TrackAsync(IDbConnectionFactory, ...) instead.
        /// Kept for backward compatibility.
        /// </summary>
        [Obsolete("Use the constructor with IDbConnectionFactory dependency injection instead.")]
        public async Task TrackAsync(
             string tableName,
             string columns,
             string dbname,
             string user,
             string host,
             int port,
             string password,
             int rowThreshold,
             int checkInterval,
             string timestampColumn,
             string trackingTable,
             string trackingId,
             CancellationToken ct)
        {
            var factory = new NpgsqlConnectionFactory(host, port, user, password, dbname);
            await TrackAsync(
                tableName,
                columns,
                rowThreshold,
                checkInterval,
                timestampColumn,
                trackingTable,
                trackingId,
                factory,
                ct);
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
        /// <param name="connectionFactory">Factory for creating database connections.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task TrackAsync(
             string tableName,
             string columns,
             int rowThreshold,
             int checkIntervalInSeconds,
             string timestampColumn,
             string trackingTable,
             string trackingId,
             IDbConnectionFactory connectionFactory,
             CancellationToken ct)
        {
            await using var conn = (NpgsqlConnection)await connectionFactory.CreateConnectionAsync(ct);

            await using var cmd = conn.CreateCommand();
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            long latestTxnId;
            long latestStructureVersion;

            // TRACKING TABLE
            if (!string.IsNullOrEmpty(trackingTable) && !string.IsNullOrEmpty(trackingId))
            {
                cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {trackingTable} (
                timestamp TIMESTAMP,
                trackingId SYMBOL,
                tableName SYMBOL,
                sequencerTxn LONG
            ) timestamp (timestamp) PARTITION BY DAY WAL DEDUP UPSERT KEYS(timestamp, trackingId, tableName);
        ";
                await cmd.ExecuteNonQueryAsync(ct);

                cmd.CommandText = $@"
            SELECT tableName, sequencerTxn
            FROM {trackingTable}
            WHERE trackingId = '{trackingId}'
            LATEST ON timestamp
            PARTITION BY tableName;
        ";

                long? foundTxn = null;

                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    if (await reader.ReadAsync(ct))
                        foundTxn = reader.GetInt64(1);
                }

                if (foundTxn.HasValue)
                {
                    latestTxnId = foundTxn.Value;

                    latestStructureVersion = (long)await cmd.ExecuteScalarFromQueryAsync(
                        $"SELECT structureVersion FROM wal_transactions('{tableName}') WHERE sequencerTxn={latestTxnId} LIMIT 1",
                        ct
                    );
                }
                else
                {
                    await using var r2 = await cmd.ExecuteReaderFromQueryAsync(
                        $"SELECT sequencerTxn, structureVersion FROM wal_transactions('{tableName}') ORDER BY sequencerTxn DESC LIMIT 1",
                        ct
                    );
                    await r2.ReadAsync(ct);
                    latestTxnId = r2.GetInt64(0);
                    latestStructureVersion = r2.GetInt64(1);
                }
            }
            else
            {
                await using var r = await cmd.ExecuteReaderFromQueryAsync(
                    $"SELECT sequencerTxn, structureVersion FROM wal_transactions('{tableName}') ORDER BY sequencerTxn DESC LIMIT 1",
                    ct
                );
                await r.ReadAsync(ct);
                latestTxnId = r.GetInt64(0);
                latestStructureVersion = r.GetInt64(1);
            }

            Console.WriteLine($"Starting from transaction ID: {latestTxnId} with structure version: {latestStructureVersion}");

            // MAIN LOOP
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(checkIntervalInSeconds * 1000, ct);

                cmd.CommandText = $@"
            SELECT sequencerTxn, minTimestamp, maxTimestamp, rowCount, structureVersion
            FROM wal_transactions('{tableName}')
            WHERE sequencerTxn > {latestTxnId}
        ";

                var newTxns = new List<(long txn, DateTime? minTs, DateTime? maxTs, long? rows, long structVer)>();

                await using (var r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        newTxns.Add((
                            r.GetInt64(0),
                            r.IsDBNull(1) ? null : r.GetDateTime(1),
                            r.IsDBNull(2) ? null : r.GetDateTime(2),
                            r.IsDBNull(3) ? null : r.GetInt64(3),
                            r.GetInt64(4)
                        ));
                    }

                    await r.CloseAsync();
                }

                if (newTxns.Count == 0)
                    continue;

                // STRUCTURE VERSION CHANGE
                foreach (var txn in newTxns)
                {
                    if (txn.structVer != latestStructureVersion)
                    {
                        Console.WriteLine($"Structure version changed from {latestStructureVersion} to {txn.structVer} on transaction {txn.txn}");
                        latestStructureVersion = txn.structVer;
                    }
                }

                long totalRows = 0;
                foreach (var t in newTxns)
                    if (t.rows.HasValue)
                        totalRows += t.rows.Value;

                if (totalRows < rowThreshold)
                    continue;

                DateTime? minTs = null;
                DateTime? maxTs = null;

                foreach (var t in newTxns)
                {
                    if (t.minTs.HasValue)
                        minTs = minTs == null ? t.minTs : (t.minTs < minTs ? t.minTs : minTs);

                    if (t.maxTs.HasValue)
                        maxTs = maxTs == null ? t.maxTs : (t.maxTs > maxTs ? t.maxTs : maxTs);
                }

                if (minTs == null || maxTs == null)
                    continue;

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

                string aggQuery = $@"
            SELECT {string.Join(", ", aggParts)}
            FROM {tableName}
            WHERE {timestampColumn} BETWEEN '{minTs:yyyy-MM-ddTHH:mm:ss}' AND '{maxTs:yyyy-MM-ddTHH:mm:ss}'
        ";

                cmd.CommandText = aggQuery;

                await using var result = await cmd.ExecuteReaderAsync(ct);
                await result.ReadAsync(ct);

                Console.WriteLine($"Aggregated results from {minTs} to {maxTs}:");
                Console.WriteLine($"Included Transactions: {newTxns[0].txn} to {newTxns[^1].txn}");
                Console.WriteLine($"Total Rows: {totalRows}");

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

                latestTxnId = newTxns[^1].txn;

                await result.CloseAsync();

                if (!string.IsNullOrEmpty(trackingTable) && !string.IsNullOrEmpty(trackingId))
                {
                    string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                    cmd.CommandText = $@"
                INSERT INTO {trackingTable} (timestamp, trackingId, tableName, sequencerTxn)
                VALUES ('{now}', '{trackingId}', '{tableName}', {latestTxnId})
            ";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                if (OnChange != null)
                {
                    if (_ui != null)
                    {
                        _ui.Post(async _ =>
                        {
                            await OnChange(new WalChangeEventArgs(
                                newTxns,
                                minTs!.Value,
                                maxTs!.Value,
                                totalRows
                            ));
                        }, null);
                    }
                    else
                    {
                        await OnChange(new WalChangeEventArgs(
                            newTxns,
                            minTs!.Value,
                            maxTs!.Value,
                            totalRows
                        ));
                    }
                }
            }

            Console.WriteLine("TrackAsync stopped.");
        }

    }
}
