# QuestDB.Change.Tracker.Api
## 1. Requirements
- V2 format of the QuestDB table.

To achive this, please add to the server.conf:
```
cairo.default.sequencer.part.txn.count=50000
```
It has to be a value greater than 1, otherwise the change tracking will not work.

In case of any issues please follow:
```
https://community.questdb.com/t/null-for-mintimestamp-maxtimestamp-and-rowcount/994
```

## 2. Usage
It is meant as a detector running i.e. as a background service.
Example of the execution:
```
    // given
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
        // then
        // inform via hub etc...
        // save to DB etc...
    };

    // when
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
```
# Co-author
 - GitHub Copilot
