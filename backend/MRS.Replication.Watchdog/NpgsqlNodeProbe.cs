using Microsoft.Extensions.Options;
using MRS.Replication.Shared;
using Npgsql;

namespace MRS.Replication.Watchdog;

public sealed class NpgsqlNodeProbe(IOptions<PostgresOptions> options, ILogger<NpgsqlNodeProbe> logger) : INodeProbe
{
    private readonly PostgresOptions _options = options.Value;

    public async Task<NodeProbeResult> ProbeAsync(NodeInfo node, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_options.BuildConnectionString(node.Host, node.Port));
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT pg_is_in_recovery();", conn);
            var result = await cmd.ExecuteScalarAsync(ct);
            var isInRecovery = result is bool b && b;
            return new NodeProbeResult(true, isInRecovery);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Probe failed for node {NodeId} ({Host}:{Port})", node.Id, node.Host, node.Port);
            return new NodeProbeResult(false, null);
        }
    }

    public async Task<Dictionary<string, (long LagBytes, double LagMs)>> GetReplicationLagAsync(NodeInfo primary, CancellationToken ct)
    {
        var result = new Dictionary<string, (long, double)>();
        try
        {
            await using var conn = new NpgsqlConnection(_options.BuildConnectionString(primary.Host, primary.Port));
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT application_name,
                       COALESCE(pg_wal_lsn_diff(pg_current_wal_lsn(), replay_lsn), 0) AS lag_bytes,
                       COALESCE(EXTRACT(MILLISECONDS FROM replay_lag), 0) AS lag_ms
                FROM pg_stat_replication;
                """, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var appName = reader.GetString(0);
                var lagBytes = reader.GetInt64(1);
                var lagMs = reader.GetDouble(2);
                result[appName] = (lagBytes, lagMs);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read pg_stat_replication from primary {NodeId}", primary.Id);
        }
        return result;
    }

    public async Task PromoteAsync(NodeInfo replica, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_options.BuildConnectionString(replica.Host, replica.Port));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT pg_promote(true, 10);", conn);
        await cmd.ExecuteScalarAsync(ct);
    }
}
