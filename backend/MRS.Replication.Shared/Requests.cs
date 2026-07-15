namespace MRS.Replication.Shared;

public record RegisterNodeRequest
{
    public string? Id { get; init; }
    public required string Name { get; init; }
    public required NodeRole Role { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int Priority { get; init; }
}

public record QueryRequest
{
    public required string Sql { get; init; }
}

public record QueryResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string[] Columns { get; init; } = [];
    public List<Dictionary<string, object?>> Rows { get; init; } = [];
    public int RowsAffected { get; init; }
    public required string TargetNodeId { get; init; }
    public required string TargetNodeName { get; init; }
    public long ElapsedMs { get; init; }
}

public record SetupRequest
{
    public required string PostgresUser { get; init; }
    public required string PostgresPassword { get; init; }
    public required string PostgresDb { get; init; }
    public int ReplicaCount { get; init; } = 1;
    public required ReplicationConfig Config { get; init; }
}

public record ReplicaCountRequest
{
    public required int Count { get; init; }
}

public record ChangeModeRequest
{
    public required ReplicationMode Mode { get; init; }
    public int? SyncTimeoutMs { get; init; }
}
