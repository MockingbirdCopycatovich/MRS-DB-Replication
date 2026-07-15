using Microsoft.Extensions.Options;
using MRS.Replication.Shared;
using Npgsql;

namespace MRS.Replication.Api;

/// <summary>
/// Applies the sync/async replication switch (spec §3.1) at the Postgres level via
/// ALTER SYSTEM — no container rebuild/restart needed, takes effect on pg_reload_conf().
/// </summary>
public sealed class PrimaryAdminService(IOptions<DockerOptions> dockerOptions, IOptions<PostgresOptions> pgOptions, ILogger<PrimaryAdminService> logger)
{
    public async Task ApplyReplicationModeAsync(PrimarySpec primary, ReplicationMode mode, IEnumerable<string> replicaIds, CancellationToken ct)
    {
        var standbyNames = mode == ReplicationMode.Sync
            ? "ANY 1 (" + string.Join(",", replicaIds.Select(id => $"\"{id}\"")) + ")"
            : string.Empty;
        var synchronousCommit = mode == ReplicationMode.Sync ? "on" : "off";

        await using var conn = new NpgsqlConnection(
            pgOptions.Value.BuildConnectionString(primary.ContainerName, dockerOptions.Value.InternalPostgresPort, timeoutSeconds: 5));
        await conn.OpenAsync(ct);

        await using (var cmd = new NpgsqlCommand($"ALTER SYSTEM SET synchronous_commit = '{synchronousCommit}';", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ALTER SYSTEM SET is a utility statement — it doesn't support bind parameters, so the
        // value is inlined as a literal. Safe here: standbyNames is built only from node ids we
        // generate ourselves (e.g. "replica-1"), never from user input.
        var escapedStandbyNames = standbyNames.Replace("'", "''");
        await using (var cmd = new NpgsqlCommand($"ALTER SYSTEM SET synchronous_standby_names = '{escapedStandbyNames}';", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand("SELECT pg_reload_conf();", conn))
        {
            await cmd.ExecuteScalarAsync(ct);
        }

        logger.LogInformation("Applied replication mode {Mode} (synchronous_standby_names='{StandbyNames}')", mode, standbyNames);
    }
}
