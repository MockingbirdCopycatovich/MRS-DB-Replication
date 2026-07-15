using System.Diagnostics;
using MRS.Replication.Shared;

namespace MRS.Replication.Api;

/// <summary>
/// Query Router / Proxy (spec §3.4): writes always go to the current primary, reads are
/// load-balanced round-robin across every Active node (primary included), each request
/// passing through that node's queue.
/// </summary>
public sealed class QueryRouterService(INodeCache nodeCache, NodeQueueService queueService, ISqlExecutor executor)
{
    private int _roundRobinCounter = -1;

    public async Task<QueryResult> ExecuteAsync(string sql, CancellationToken ct)
    {
        var kind = SqlClassifier.Classify(sql);
        var target = kind == SqlKind.Write ? nodeCache.CurrentPrimary : PickReadTarget();

        if (target is null)
        {
            return new QueryResult
            {
                Success = false,
                Error = kind == SqlKind.Write ? "No active primary available" : "No active node available for reads",
                TargetNodeId = string.Empty,
                TargetNodeName = string.Empty
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await queueService.EnqueueAsync(target.Id, ct2 => executor.ExecuteAsync(target, sql, ct2), ct);
        stopwatch.Stop();
        return result with { ElapsedMs = stopwatch.ElapsedMilliseconds };
    }

    internal NodeInfo? PickReadTarget()
    {
        var candidates = nodeCache.Snapshot().Where(n => n.Status == NodeStatus.Active).ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }
        var index = (uint)Interlocked.Increment(ref _roundRobinCounter) % (uint)candidates.Length;
        return candidates[index];
    }
}
