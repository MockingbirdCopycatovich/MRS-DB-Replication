using System.Collections.Concurrent;
using MRS.Replication.Shared;

namespace MRS.Replication.Api;

public interface INodeCache
{
    NodeInfo? CurrentPrimary { get; }
    IReadOnlyCollection<NodeInfo> Snapshot();
}

/// <summary>
/// Mirrors Watchdog's node registry locally via an initial GET /status plus the live SSE
/// event stream — this is how the Query Router "hears about" a new primary without polling
/// on every query (per spec §3.3's "notifies Query Router of new primary").
/// </summary>
public sealed class LiveNodeCache(IWatchdogClient watchdogClient, ILogger<LiveNodeCache> logger) : BackgroundService, INodeCache
{
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();

    public NodeInfo? CurrentPrimary =>
        _nodes.Values.FirstOrDefault(n => n.Role == NodeRole.Primary && n.Status == NodeStatus.Active);

    public IReadOnlyCollection<NodeInfo> Snapshot() => _nodes.Values.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Lost connection to Watchdog event stream, retrying");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var initial = await watchdogClient.GetStatusAsync(ct);
        _nodes.Clear();
        foreach (var node in initial)
        {
            _nodes[node.Id] = node;
        }

        await foreach (var evt in watchdogClient.StreamEventsAsync(ct))
        {
            if (evt.Type == NodeEventType.Removed)
            {
                _nodes.TryRemove(evt.NodeId, out _);
                continue;
            }

            if (evt.Node is not null)
            {
                _nodes[evt.NodeId] = evt.Node;
            }
        }
    }
}
