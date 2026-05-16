using NUnit.Framework;
using QuestDB.Change.Tracker.Api;
using QuestDB.Change.Tracker.Api.Model.Connection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace UT.ConfigurationManager.Api
{
    /// <summary>
    /// Integration tests for TrackChangesEngine.
    /// These tests require a running QuestDB instance at localhost:8812.
    /// Indjest data into the "123" table with a "timestamp" column for testing.
    /// </summary>
    public class When_tracker_is_in_use
    {
        [Test, Ignore("Temporarily disabled")]
        public async Task I_d_like_to_check_onchange_event()
        {
            //given 
            var connectionFactory = new NpgsqlConnectionFactory(
                host: "127.0.0.1",
                port: 8812,
                username: "admin",
                password: "quest",
                database: "qdb"
            );
            var tracker = new TrackChangesEngine(connectionFactory);
            tracker.OnChange += async (args) =>
            {
                //then
                await Task.Yield();
                Assert.Pass("Change event received");
            };

            //when
            var cts = new CancellationTokenSource();
            await Task.Run(() =>
                tracker.TrackAsync(
                    tableName: "123",
                    columns: "TM5_10",
                    rowThreshold: 1,
                    checkIntervalInSeconds: 1,
                    timestampColumn: "timestamp",
                    trackingTable: "trackingTable",
                    trackingId: Guid.NewGuid().ToString(),
                    ct: cts.Token
                )
            );
        }
    }
}
