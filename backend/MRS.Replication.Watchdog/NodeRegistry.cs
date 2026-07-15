using System.Collections.Concurrent;
using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

public sealed record NodeUpdateResult(NodeInfo? Node, List<NodeEvent> Events, bool BecamePrimaryDown, bool BecameResyncReady);

/// <summary>
/// Thread-safe store of registered nodes and their status state machine:
/// Active/Delayed/Provisioning --(N failed probes)--> Inactive
/// Inactive/Failed             --(probe succeeds again)--> Resyncing   (never straight back to Active)
/// Resyncing                   --(caught up / timeout, driven externally)--> Active | Failed
/// </summary>
public sealed class NodeRegistry
{
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();

    public NodeInfo Register(RegisterNodeRequest request)
    {
        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N")[..12] : request.Id!;
        var node = new NodeInfo
        {
            Id = id,
            Name = request.Name,
            Role = request.Role,
            Status = NodeStatus.Provisioning,
            Host = request.Host,
            Port = request.Port,
            Priority = request.Priority,
            RegisteredAt = DateTimeOffset.UtcNow,
            LastCheckedAt = DateTimeOffset.UtcNow
        };
        _nodes[id] = node;
        return node;
    }

    public bool Remove(string id) => _nodes.TryRemove(id, out _);

    public NodeInfo? Get(string id) => _nodes.GetValueOrDefault(id);

    public IReadOnlyCollection<NodeInfo> GetAll() => _nodes.Values.ToArray();

    public NodeInfo? GetPrimary() => _nodes.Values.FirstOrDefault(n => n.Role == NodeRole.Primary);

    public void SetRole(string id, NodeRole role)
    {
        _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, n) => n with { Role = role });
    }

    public NodeInfo? SetStatus(string id, NodeStatus status)
    {
        NodeInfo? updated = null;
        _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, n) =>
        {
            updated = n with { Status = status, LastCheckedAt = DateTimeOffset.UtcNow };
            return updated;
        });
        return updated;
    }

    public void UpdateQueueDepth(string id, int depth)
    {
        _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, n) => n with { QueueDepth = depth });
    }

    /// <summary>Refreshes lag numbers only (from the primary's pg_stat_replication), independent of the reachability state machine.</summary>
    public void UpdateLag(string id, long lagBytes, double lagMs)
    {
        _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, n) =>
            n.Status is NodeStatus.Active or NodeStatus.Delayed ? n with { LagBytes = lagBytes, LagMs = lagMs } : n);
    }

    /// <summary>Applies one probe cycle result to a node and runs the status state machine. Returns events to publish and flags for the caller to react to (trigger failover / resync).</summary>
    public NodeUpdateResult RecordProbeResult(string id, bool reachable, long? lagBytes, double? lagMs, ReplicationConfig config)
    {
        var events = new List<NodeEvent>();
        var becamePrimaryDown = false;
        var becameResyncReady = false;

        NodeInfo? finalNode = null;

        _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, node) =>
        {
            var previousStatus = node.Status;
            NodeInfo updated;

            if (node.Status == NodeStatus.Resyncing)
            {
                // Owned by ResyncService — monitor only refreshes lastCheckedAt.
                finalNode = node with { LastCheckedAt = DateTimeOffset.UtcNow };
                return finalNode;
            }

            if (reachable)
            {
                var newLagBytes = lagBytes ?? node.LagBytes;
                var newLagMs = lagMs ?? node.LagMs;

                NodeStatus newStatus = previousStatus switch
                {
                    NodeStatus.Provisioning => NodeStatus.Active,
                    NodeStatus.Active or NodeStatus.Delayed =>
                        newLagBytes > config.DelayedLagBytesThreshold ? NodeStatus.Delayed : NodeStatus.Active,
                    NodeStatus.Inactive or NodeStatus.Failed => NodeStatus.Resyncing,
                    _ => previousStatus
                };

                updated = node with
                {
                    Status = newStatus,
                    ConsecutiveFailures = 0,
                    LagBytes = newLagBytes,
                    LagMs = newLagMs,
                    LastCheckedAt = DateTimeOffset.UtcNow
                };

                if (newStatus == NodeStatus.Resyncing && previousStatus is NodeStatus.Inactive or NodeStatus.Failed)
                {
                    becameResyncReady = true;
                    events.Add(new NodeEvent { NodeId = id, Type = NodeEventType.StatusChanged, Message = $"{node.Name} reachable again — starting mandatory resync before returning to rotation", Node = updated });
                }
                else if (newStatus != previousStatus)
                {
                    events.Add(new NodeEvent { NodeId = id, Type = NodeEventType.StatusChanged, Message = $"{node.Name} {previousStatus} -> {newStatus}", Node = updated });
                }
            }
            else
            {
                var failures = node.ConsecutiveFailures + 1;
                var newStatus = previousStatus;

                if (failures >= config.FailuresBeforeInactive && previousStatus is NodeStatus.Active or NodeStatus.Delayed or NodeStatus.Provisioning)
                {
                    newStatus = NodeStatus.Inactive;
                }

                updated = node with
                {
                    Status = newStatus,
                    ConsecutiveFailures = failures,
                    LastCheckedAt = DateTimeOffset.UtcNow
                };

                if (newStatus != previousStatus)
                {
                    events.Add(new NodeEvent { NodeId = id, Type = NodeEventType.StatusChanged, Message = $"{node.Name} {previousStatus} -> {newStatus} after {failures} failed probes", Node = updated });
                    if (newStatus == NodeStatus.Inactive && node.Role == NodeRole.Primary)
                    {
                        becamePrimaryDown = true;
                    }
                }
            }

            finalNode = updated;
            return updated;
        });

        return new NodeUpdateResult(finalNode, events, becamePrimaryDown, becameResyncReady);
    }
}
