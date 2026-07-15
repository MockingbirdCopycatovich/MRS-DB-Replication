using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

public record NodeProbeResult(bool Reachable, bool? IsInRecovery);

/// <summary>Postgres-facing operations Watchdog needs: reachability/role probing, replication lag (from primary), and promotion.</summary>
public interface INodeProbe
{
    Task<NodeProbeResult> ProbeAsync(NodeInfo node, CancellationToken ct);

    /// <summary>Queried against the primary; returns lag per connected standby keyed by application_name (== replica NodeId).</summary>
    Task<Dictionary<string, (long LagBytes, double LagMs)>> GetReplicationLagAsync(NodeInfo primary, CancellationToken ct);

    Task PromoteAsync(NodeInfo replica, CancellationToken ct);
}
