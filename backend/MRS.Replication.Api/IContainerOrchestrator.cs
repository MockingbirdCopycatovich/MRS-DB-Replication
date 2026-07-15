namespace MRS.Replication.Api;

public sealed record ManagedContainer(string Name, string Id, string State, IReadOnlyDictionary<string, string> Labels);

/// <summary>Abstraction over the Docker Engine API so Replica Manager logic is unit-testable without a real Docker daemon.</summary>
public interface IContainerOrchestrator
{
    Task EnsurePrimaryAsync(PrimarySpec spec, CancellationToken ct);

    Task EnsureReplicaAsync(ReplicaSpec spec, PrimarySpec primary, CancellationToken ct);

    Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken ct);

    Task StartAsync(string containerName, CancellationToken ct);

    Task RemoveAsync(string containerName, CancellationToken ct);
}
