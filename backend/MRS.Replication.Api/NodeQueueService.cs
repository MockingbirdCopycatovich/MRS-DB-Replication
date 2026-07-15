using System.Collections.Concurrent;

namespace MRS.Replication.Api;

/// <summary>
/// Per-node request queue (spec §3.4): buffers queries while a primary switch or a
/// delayed replica is in progress, and reports queue depth for the frontend sidebar.
/// </summary>
public sealed class NodeQueueService
{
    private readonly ConcurrentDictionary<string, NodeQueue> _queues = new();

    public Task<T> EnqueueAsync<T>(string nodeId, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        var queue = _queues.GetOrAdd(nodeId, _ => new NodeQueue());
        return queue.RunAsync(work, ct);
    }

    public int DepthOf(string nodeId) => _queues.TryGetValue(nodeId, out var queue) ? queue.Depth : 0;

    private sealed class NodeQueue
    {
        private const int MaxConcurrency = 2;
        private readonly SemaphoreSlim _semaphore = new(MaxConcurrency, MaxConcurrency);
        private int _depth;

        public int Depth => Volatile.Read(ref _depth);

        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            Interlocked.Increment(ref _depth);
            try
            {
                await _semaphore.WaitAsync(ct);
                try
                {
                    return await work(ct);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _depth);
            }
        }
    }
}
