using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

/// <summary>Runtime-mutable replication config (mode, thresholds, timings) — no rebuild/redeploy needed.</summary>
public sealed class ConfigStore
{
    private readonly object _lock = new();
    private ReplicationConfig _current;

    public ConfigStore(IConfiguration configuration)
    {
        _current = new ReplicationConfig
        {
            Mode = Enum.TryParse<ReplicationMode>(configuration["Replication:Mode"], out var mode) ? mode : ReplicationMode.Async,
            HealthCheckIntervalMs = configuration.GetValue("Replication:HealthCheckIntervalMs", 3000),
            FailuresBeforeInactive = configuration.GetValue("Replication:FailuresBeforeInactive", 3),
            SyncTimeoutMs = configuration.GetValue("Replication:SyncTimeoutMs", 5000),
            DelayedLagBytesThreshold = configuration.GetValue<long>("Replication:DelayedLagBytesThreshold", 8 * 1024 * 1024),
            ResyncCaughtUpLagBytesThreshold = configuration.GetValue<long>("Replication:ResyncCaughtUpLagBytesThreshold", 64 * 1024),
            ResyncTimeoutMs = configuration.GetValue("Replication:ResyncTimeoutMs", 120_000),
            MinActiveNodes = configuration.GetValue("Replication:MinActiveNodes", 1),
            MaxInactiveNodes = configuration.GetValue("Replication:MaxInactiveNodes", 0)
        };
    }

    public ReplicationConfig Current
    {
        get { lock (_lock) return _current; }
    }

    public void Update(ReplicationConfig config)
    {
        lock (_lock) { _current = config; }
    }
}
