using Npgsql;
using System.Threading;
using System.Threading.Tasks;

namespace QuestDB.Change.Tracker.Api
{
    public static class NpgsqlExtensions
    {
        public static async Task<NpgsqlDataReader> ExecuteReaderFromQueryAsync(this NpgsqlCommand cmd, string sql, CancellationToken ct)
        {
            cmd.CommandText = sql;
            return await    cmd.ExecuteReaderAsync(ct);
        }

        public static async Task<object> ExecuteScalarFromQueryAsync(this NpgsqlCommand cmd, string sql, CancellationToken ct)
        {
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(ct);
        }
    }
}
