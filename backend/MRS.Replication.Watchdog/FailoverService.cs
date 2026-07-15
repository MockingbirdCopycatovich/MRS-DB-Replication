using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

/// <summary>
/// Promotes the healthiest active replica when the primary is detected as down.
/// Never re-promotes the old primary automatically — it must go through ResyncService
/// and always rejoins as a Replica, which is what prevents split-brain.
/// </summary>
public sealed class FailoverService(NodeRegistry registry, EventBus bus, INodeProbe probe, ResyncService resyncService, ILogger<FailoverService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task HandlePrimaryDownAsync(string oldPrimaryId, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            return; // failover already in progress
        }

        try
        {
            if (registry.GetAll().Any(n => n.Role == NodeRole.Primary && n.Status == NodeStatus.Active))
            {
                return; // someone already promoted (e.g. a previous cycle) — nothing to do
            }

            var candidate = registry.GetAll()
                .Where(n => n.Role == NodeRole.Replica && n.Status == NodeStatus.Active)
                .OrderBy(n => n.LagBytes)
                .ThenBy(n => n.Priority)
                .ThenBy(n => n.RegisteredAt)
                .FirstOrDefault();

            if (candidate is null)
            {
                bus.Publish(new NodeEvent
                {
                    NodeId = oldPrimaryId,
                    Type = NodeEventType.Alert,
                    Message = "Primary is down and no healthy replica is available for failover"
                });
                return;
            }

            bus.Publish(new NodeEvent
            {
                NodeId = candidate.Id,
                Type = NodeEventType.FailoverStarted,
                Message = $"Promoting {candidate.Name} (lag={candidate.LagBytes} bytes) to primary"
            });

            try
            {
                await probe.PromoteAsync(candidate, ct);

                var promoted = false;
                for (var i = 0; i < 10 && !promoted; i++)
                {
                    await Task.Delay(1000, ct);
                    var check = await probe.ProbeAsync(candidate, ct);
                    if (check.Reachable && check.IsInRecovery == false)
                    {
                        promoted = true;
                    }
                }

                if (!promoted)
                {
                    throw new InvalidOperationException($"{candidate.Name} did not leave recovery mode after pg_promote()");
                }

                registry.SetRole(candidate.Id, NodeRole.Primary);
                var updated = registry.SetStatus(candidate.Id, NodeStatus.Active);

                bus.Publish(new NodeEvent
                {
                    NodeId = candidate.Id,
                    Type = NodeEventType.FailoverCompleted,
                    Message = $"{candidate.Name} promoted to primary",
                    Node = updated
                });

                RepointSurvivingReplicas(candidate.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failover to {NodeId} failed", candidate.Id);
                bus.Publish(new NodeEvent
                {
                    NodeId = candidate.Id,
                    Type = NodeEventType.Alert,
                    Message = $"Failover to {candidate.Name} failed: {ex.Message}"
                });
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Every other surviving replica was still streaming from the OLD primary and is now
    /// orphaned (Postgres won't error on this by itself — it just silently stops advancing).
    /// Force them through the same mandatory re-sync path as a returning node, but pointed at
    /// the newly promoted primary, so they don't sit "Active" while quietly going stale forever.
    /// </summary>
    private void RepointSurvivingReplicas(string newPrimaryId, CancellationToken ct)
    {
        var stale = registry.GetAll()
            .Where(n => n.Role == NodeRole.Replica && n.Status == NodeStatus.Active && n.Id != newPrimaryId)
            .ToArray();

        foreach (var replica in stale)
        {
            var updated = registry.SetStatus(replica.Id, NodeStatus.Resyncing);
            bus.Publish(new NodeEvent
            {
                NodeId = replica.Id,
                Type = NodeEventType.StatusChanged,
                Message = $"{replica.Name} was streaming from the old primary — re-syncing against the new primary",
                Node = updated
            });
            _ = resyncService.RunAsync(replica.Id, ct);
        }
    }
}
