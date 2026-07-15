namespace MRS.Replication.Shared;

public record ReplicationConfig
{
    public ReplicationMode Mode { get; init; } = ReplicationMode.Async;

    /// <summary>Max time to wait for a synchronous replica ack before the write is considered failed (sync mode only).</summary>
    public int SyncTimeoutMs { get; init; } = 5000;

    public int HealthCheckIntervalMs { get; init; } = 3000;

    /// <summary>Consecutive failed probes before a node is marked Inactive.</summary>
    public int FailuresBeforeInactive { get; init; } = 3;

    /// <summary>Replication lag (bytes) above which an Active replica is shown as Delayed.</summary>
    public long DelayedLagBytesThreshold { get; init; } = 8 * 1024 * 1024;

    /// <summary>Replication lag (bytes) below which a Resyncing node is considered caught up.</summary>
    public long ResyncCaughtUpLagBytesThreshold { get; init; } = 64 * 1024;

    public int ResyncTimeoutMs { get; init; } = 120_000;

    /// <summary>Frontend alert threshold: show a warning if fewer than this many nodes are Active.</summary>
    public int MinActiveNodes { get; init; } = 1;

    /// <summary>Frontend alert threshold: show a warning if more than this many nodes are Inactive/Failed.</summary>
    public int MaxInactiveNodes { get; init; } = 0;
}
