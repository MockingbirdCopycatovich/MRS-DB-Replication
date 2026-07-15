namespace MRS.Replication.Api;

public sealed record PrimarySpec(string ContainerName, int HostPort, string User, string Password, string Db);

public sealed record ReplicaSpec(string Id, string ContainerName, int HostPort);

/// <summary>
/// The "desired state" half of the reconciliation loop (Kubernetes-lite, per spec §3.2):
/// what SHOULD exist, kept separate from what Docker reports actually exists.
/// In-memory only — this is a demo-scoped scaling knob, not a durability requirement.
/// </summary>
public sealed class DesiredStateStore
{
    private readonly object _lock = new();
    private readonly List<ReplicaSpec> _replicas = [];
    private int _nextReplicaSeq = 1;

    public PrimarySpec? Primary { get; private set; }

    public void SetPrimary(PrimarySpec spec)
    {
        lock (_lock) { Primary = spec; }
    }

    public IReadOnlyList<ReplicaSpec> GetReplicas()
    {
        lock (_lock) { return _replicas.ToList(); }
    }

    public void ScaleTo(int count, int hostPortRangeStart)
    {
        lock (_lock)
        {
            while (_replicas.Count < count)
            {
                var seq = _nextReplicaSeq++;
                var id = $"replica-{seq}";
                _replicas.Add(new ReplicaSpec(id, $"mrs-postgres-{id}", hostPortRangeStart + seq - 1));
            }
            while (_replicas.Count > count)
            {
                _replicas.RemoveAt(_replicas.Count - 1);
            }
        }
    }

    public bool RemoveReplica(string id)
    {
        lock (_lock) { return _replicas.RemoveAll(r => r.Id == id) > 0; }
    }
}
