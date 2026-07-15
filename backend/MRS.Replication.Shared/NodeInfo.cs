namespace MRS.Replication.Shared;

public record NodeInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required NodeRole Role { get; init; }
    public NodeStatus Status { get; init; } = NodeStatus.Provisioning;
    public required string Host { get; init; }
    public required int Port { get; init; }
    public long LagBytes { get; init; }
    public double LagMs { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int QueueDepth { get; init; }
    public int Priority { get; init; }
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
