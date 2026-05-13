using NUnit.Framework;
using QuestDB.Change.Tracker.Api;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace UT.ConfigurationManager.Api
{
    /// <summary>
    /// Unit tests for TrackChangesEngine with mocked database connections.
    /// These tests do NOT require a running database instance.
    /// </summary>
    public class When_tracker_is_initialized
    {
        [Test]
        public void I_can_create_a_tracker_with_connection_factory()
        {
            // Arrange
            var mockFactory = new MockDbConnectionFactory();

            // Act
            var tracker = new TrackChangesEngine(mockFactory);

            // Assert
            Assert.That(tracker, Is.Not.Null);
        }

        [Test]
        public async Task I_can_create_a_tracker_with_default_constructor()
        {
            // Arrange & Act
            var tracker = new TrackChangesEngine();

            // Assert
            Assert.That(tracker, Is.Not.Null);
            await Task.CompletedTask;
        }

        [Test]
        public async Task I_can_track_with_mocked_connection()
        {
            // Arrange
            var mockFactory = new MockDbConnectionFactory();
            var tracker = new TrackChangesEngine(mockFactory);
            var changeReceived = false;

            tracker.OnChange += async (args) =>
            {
                changeReceived = true;
                await Task.Yield();
            };

            var cts = new CancellationTokenSource();

            // Cancel immediately to avoid long-running loop
            cts.CancelAfter(100);

            // Act & Assert
            try
            {
                await tracker.TrackAsync(
                    tableName: "test_table",
                    columns: "col1,col2",
                    rowThreshold: 1,
                    checkInterval: 1,
                    timestampColumn: "ts",
                    trackingTable: "tracking",
                    trackingId: Guid.NewGuid().ToString(),
                    connectionFactory: mockFactory,
                    ct: cts.Token
                );
            }
            catch (OperationCanceledException)
            {
                // Expected - we cancelled the token
            }

            // The mock connection factory was called at least once
            Assert.That(mockFactory.CreateConnectionCallCount, Is.GreaterThan(0));
        }
    }

    /// <summary>
    /// Mock implementation of IDbConnectionFactory for testing.
    /// Returns a mock database connection that does not perform real database operations.
    /// </summary>
    public class MockDbConnectionFactory : IDbConnectionFactory
    {
        public int CreateConnectionCallCount { get; private set; }

        public async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
        {
            CreateConnectionCallCount++;
            var mockConnection = new MockDbConnection();
            await Task.Yield(); // Simulate async behavior
            return mockConnection;
        }
    }

    /// <summary>
    /// Mock database connection that returns empty result sets and succeeds all operations.
    /// </summary>
    public class MockDbConnection : DbConnection
    {
        public override string ConnectionString { get; set; } = "mock://connection";
        public override string Database => "mock_db";
        public override string DataSource => "mock_datasource";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void Open()
        {
            // Do nothing - already "open"
        }

        public override void Close()
        {
            // Do nothing - already "closed" conceptually
        }

        protected override DbCommand CreateDbCommand()
        {
            return new MockDbCommand(this);
        }

        public override void ChangeDatabase(string databaseName)
        {
            // Do nothing
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException("Mock connection does not support transactions");
        }
    }

    /// <summary>
    /// Mock database command that returns empty result sets.
    /// </summary>
    public class MockDbCommand : DbCommand
    {
        private readonly MockDbConnection _connection;

        public MockDbCommand(MockDbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => new DbParameterCollectionMock();
        protected override DbTransaction DbTransaction { get; set; } = null!;

        public override void Cancel()
        {
            // Do nothing
        }

        public override void Prepare()
        {
            // Do nothing
        }

        protected override DbParameter CreateDbParameter()
        {
            return new MockDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return new MockDbDataReader();
        }

        public override int ExecuteNonQuery()
        {
            return 0;
        }

        public override object? ExecuteScalar()
        {
            return null;
        }
    }

    /// <summary>
    /// Mock database data reader.
    /// </summary>
    public class MockDbDataReader : DbDataReader
    {
        public override object this[int ordinal] => throw new NotImplementedException();
        public override object this[string name] => throw new NotImplementedException();
        public override int Depth => 0;
        public override int FieldCount => 0;
        public override bool HasRows => false;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;

        public override bool GetBoolean(int ordinal) => false;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => '\0';
        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => "unknown";
        public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;
        public override decimal GetDecimal(int ordinal) => 0;
        public override double GetDouble(int ordinal) => 0;
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override float GetFloat(int ordinal) => 0;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 0;
        public override long GetInt64(int ordinal) => 0;
        public override string GetName(int ordinal) => "";
        public override int GetOrdinal(string name) => 0;
        public override string GetString(int ordinal) => "";
        public override object GetValue(int ordinal) => null!;
        public override int GetValues(object[] values) => 0;
        public override bool IsDBNull(int ordinal) => true;
        public override bool NextResult() => false;
        public override bool Read() => false;

        public override IEnumerator<string> GetEnumerator()
        {
            return new List<string>().GetEnumerator();
        }
    }

    /// <summary>
    /// Mock parameter collection.
    /// </summary>
    public class DbParameterCollectionMock : DbParameterCollection
    {
        public override int Count => 0;
        public override object SyncRoot => this;

        public override int Add(object value) => 0;
        public override void AddRange(Array values) { }
        public override void Clear() { }
        public override bool Contains(object value) => false;
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) { }
        public override IEnumerator GetEnumerator() => new List<object>().GetEnumerator();
        public override int IndexOf(object value) => -1;
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) { }
        public override void Remove(object value) { }
        public override void RemoveAt(int index) { }
        public override void RemoveAt(string parameterName) { }

        protected override DbParameter GetParameter(int index) => new MockDbParameter();
        protected override DbParameter GetParameter(string parameterName) => new MockDbParameter();
        protected override void SetParameter(int index, DbParameter value) { }
        protected override void SetParameter(string parameterName, DbParameter value) { }
    }

    /// <summary>
    /// Mock database parameter.
    /// </summary>
    public class MockDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }
}

