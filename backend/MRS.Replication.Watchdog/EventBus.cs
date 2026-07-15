using System.Collections.Concurrent;
using System.Threading.Channels;
using MRS.Replication.Shared;

namespace MRS.Replication.Watchdog;

public sealed record EventSubscription(ChannelReader<NodeEvent> Reader, IDisposable Handle);

/// <summary>Fan-out pub/sub for NodeEvents, consumed by SSE clients (GET /events).</summary>
public sealed class EventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<NodeEvent>> _subscribers = new();
    private readonly ILogger<EventBus> _logger;

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    public EventSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<NodeEvent>();
        _subscribers[id] = channel;
        return new EventSubscription(channel.Reader, new Unsubscriber(_subscribers, id));
    }

    public void Publish(NodeEvent evt)
    {
        _logger.LogInformation("[{Type}] node={NodeId} {Message}", evt.Type, evt.NodeId, evt.Message);
        foreach (var (_, channel) in _subscribers)
        {
            channel.Writer.TryWrite(evt);
        }
    }

    private sealed class Unsubscriber(ConcurrentDictionary<Guid, Channel<NodeEvent>> subscribers, Guid id) : IDisposable
    {
        public void Dispose() => subscribers.TryRemove(id, out _);
    }
}
