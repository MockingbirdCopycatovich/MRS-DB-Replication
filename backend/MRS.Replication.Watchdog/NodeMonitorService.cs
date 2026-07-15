using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

/// <summary>Periodically probes every registered node and drives the status state machine, reacting to failover/resync triggers.</summary>
public sealed class NodeMonitorService(
    NodeRegistry registry,
    EventBus bus,
    INodeProbe probe,
    ConfigStore configStore,
    FailoverService failoverService,
    ResyncService resyncService,
    ILogger<NodeMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Monitor cycle failed");
            }

            await Task.Delay(configStore.Current.HealthCheckIntervalMs, stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var config = configStore.Current;
        var nodes = registry.GetAll();

        // Probe every node uniformly, regardless of role. Deliberately NOT special-cased to
        // "the" Role==Primary node: right after a failover there can transiently be two nodes
        // labeled Role=Primary (the newly promoted one, and the old one still waiting to go
        // through mandatory resync) — singling one out here previously meant the other stopped
        // being probed at all and got stuck Inactive forever.
        foreach (var node in nodes)
        {
            if (node.Status == NodeStatus.Resyncing)
            {
                continue; // owned by ResyncService while it re-syncs
            }

            var probeResult = await probe.ProbeAsync(node, ct);
            Apply(node.Id, probeResult, config, ct);
        }

        var currentPrimary = registry.GetAll().FirstOrDefault(n => n.Role == NodeRole.Primary && n.Status == NodeStatus.Active);
        if (currentPrimary is not null)
        {
            var lagMap = await probe.GetReplicationLagAsync(currentPrimary, ct);
            foreach (var (nodeId, lag) in lagMap)
            {
                var node = registry.Get(nodeId);
                if (node is { Role: NodeRole.Replica })
                {
                    registry.UpdateLag(nodeId, lag.LagBytes, lag.LagMs);
                }
            }
        }
    }

    private void Apply(string nodeId, NodeProbeResult probeResult, ReplicationConfig config, CancellationToken ct)
    {
        var result = registry.RecordProbeResult(nodeId, probeResult.Reachable, null, null, config);

        foreach (var evt in result.Events)
        {
            bus.Publish(evt);
        }

        if (result.BecamePrimaryDown)
        {
            _ = failoverService.HandlePrimaryDownAsync(nodeId, ct);
        }

        if (result.BecameResyncReady)
        {
            _ = resyncService.RunAsync(nodeId, ct);
        }
    }
}
