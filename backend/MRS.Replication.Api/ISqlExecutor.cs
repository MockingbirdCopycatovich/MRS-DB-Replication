using Microsoft.Extensions.Options;
using MRS.Replication.Shared;
using Npgsql;

namespace MRS.Replication.Api;

/// <summary>Runs a SQL statement against a specific node — isolated behind an interface so QueryRouterService's routing logic is testable without a live Postgres.</summary>
public interface ISqlExecutor
{
    Task<QueryResult> ExecuteAsync(NodeInfo node, string sql, CancellationToken ct);
}

public sealed class NpgsqlSqlExecutor(IOptions<PostgresOptions> pgOptions) : ISqlExecutor
{
    public async Task<QueryResult> ExecuteAsync(NodeInfo node, string sql, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(pgOptions.Value.BuildConnectionString(node.Host, node.Port, timeoutSeconds: 10));
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }

            var recordsAffected = reader.RecordsAffected;

            return new QueryResult
            {
                Success = true,
                Columns = columns,
                Rows = rows,
                RowsAffected = recordsAffected < 0 ? 0 : recordsAffected,
                TargetNodeId = node.Id,
                TargetNodeName = node.Name
            };
        }
        catch (Exception ex)
        {
            return new QueryResult
            {
                Success = false,
                Error = ex.Message,
                TargetNodeId = node.Id,
                TargetNodeName = node.Name
            };
        }
    }
}
