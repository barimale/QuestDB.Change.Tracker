using NUnit.Framework;
using QuestDB.Change.Tracker.Api;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static Npgsql.Replication.PgOutput.Messages.RelationMessage;

namespace UT.ConfigurationManager.Api
{
    public class When_tracker_is_in_use
    {
        [Test]
        public async Task I_d_like_to_get_specific_appSetting_using_lazy_adapter()
        {
            //given 
            var tracker = new TrackChangesEngine();
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
                    "123",
                    "TM5_10",
                    "qdb",
                    "admin",
                    "127.0.0.1",
                    8812,
                    "quest",
                    10,
                    1,
                    "timestamp",
                    "trackingTable",
                    Guid.NewGuid().ToString(),
                    cts.Token
                )
            );
        }
    }
}