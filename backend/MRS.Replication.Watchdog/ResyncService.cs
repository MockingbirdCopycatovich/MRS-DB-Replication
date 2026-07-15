using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

/// <summary>
/// Drives a returning node through mandatory re-sync before it's allowed back into rotation.
/// A node NEVER jumps straight from Inactive/Failed to Active — it must pass through here.
/// </summary>
public sealed class ResyncService(
    NodeRegistry registry,
    EventBus bus,
    INodeProbe probe,
    IReplicaManagerClient replicaManagerClient,
    ConfigStore configStore,
    ILogger<ResyncService> logger)
{
    public async Task RunAsync(string nodeId, CancellationToken ct)
    {
        var node = registry.Get(nodeId);
        if (node is null || node.Status != NodeStatus.Resyncing)
        {
            return;
        }

        var config = configStore.Current;
        var primary = registry.GetPrimary();

        if (node.Role == NodeRole.Primary && primary?.Id == nodeId)
        {
            // No failover ever promoted a replacement while this node was down (e.g. no replica
            // was available) — it never actually lost the Primary role, so there is no other
            // writer it could conflict with and nothing to catch up on. The Replica Manager
            // deliberately does NOT auto-restart the primary container (that would race this
            // very decision) — so ask it to start the container, then confirm reachability
            // before flipping the registry back to Active.
            await replicaManagerClient.StartNodeAsync(nodeId, ct);

            var resumeDeadline = DateTimeOffset.UtcNow.AddMilliseconds(config.ResyncTimeoutMs);
            while (DateTimeOffset.UtcNow < resumeDeadline)
            {
                await Task.Delay(2000, ct);
                var probeResult = await probe.ProbeAsync(node, ct);
                if (probeResult.Reachable && probeResult.IsInRecovery == false)
                {
                    var resumed = registry.SetStatus(nodeId, NodeStatus.Active);
                    bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.ResyncCompleted, Message = $"{node.Name} resumes as primary — no failover occurred while it was down", Node = resumed });
                    return;
                }
            }

            logger.LogWarning("Primary {NodeId} did not become reachable again after restart", nodeId);
            registry.SetStatus(nodeId, NodeStatus.Failed);
            bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.Alert, Message = $"{node.Name} could not be restarted as primary" });
            return;
        }

        if (primary is null)
        {
            registry.SetStatus(nodeId, NodeStatus.Failed);
            bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.Alert, Message = $"{node.Name} cannot resync: no active primary to sync from" });
            return;
        }

        bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.ResyncStarted, Message = $"Re-syncing {node.Name} from primary {primary.Name} before returning it to rotation" });

        var triggered = await replicaManagerClient.TriggerResyncAsync(nodeId, primary.Host, primary.Port, ct);
        if (!triggered)
        {
            registry.SetStatus(nodeId, NodeStatus.Failed);
            bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.Alert, Message = $"{node.Name} resync could not be triggered on Replica Manager" });
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(config.ResyncTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(2000, ct);

            var probeResult = await probe.ProbeAsync(node, ct);
            if (!probeResult.Reachable || probeResult.IsInRecovery != true)
            {
                continue; // still bootstrapping (pg_basebackup running, postgres not up yet)
            }

            var lagMap = await probe.GetReplicationLagAsync(primary, ct);
            if (lagMap.TryGetValue(nodeId, out var lag) && lag.LagBytes <= config.ResyncCaughtUpLagBytesThreshold)
            {
                registry.SetRole(nodeId, NodeRole.Replica); // never re-admitted as primary — avoids split-brain
                var updated = registry.SetStatus(nodeId, NodeStatus.Active);
                bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.ResyncCompleted, Message = $"{node.Name} caught up (lag={lag.LagBytes}B) and is active again as a replica", Node = updated });
                return;
            }
        }

        logger.LogWarning("Resync timed out for node {NodeId}", nodeId);
        registry.SetStatus(nodeId, NodeStatus.Failed);
        bus.Publish(new NodeEvent { NodeId = nodeId, Type = NodeEventType.Alert, Message = $"{node.Name} resync timed out — marked Failed, stays out of rotation" });
    }
}
