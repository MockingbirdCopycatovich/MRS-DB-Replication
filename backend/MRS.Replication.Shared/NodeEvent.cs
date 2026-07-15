namespace MRS.Replication.Shared;

public record NodeEvent
{
    public required string NodeId { get; init; }
    public required NodeEventType Type { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string Message { get; init; }
    public NodeInfo? Node { get; init; }
}
