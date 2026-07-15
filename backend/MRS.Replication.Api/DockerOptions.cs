namespace MRS.Replication.Api;

public sealed class DockerOptions
{
    public string SocketPath { get; set; } = "unix:///var/run/docker.sock";
    public string NetworkName { get; set; } = "mrs-net";
    public string Image { get; set; } = "mrs-postgres-node:latest";
    public string StackLabel { get; set; } = "mrs.stack";
    public string StackLabelValue { get; set; } = "replicator";
    public string PrimaryContainerName { get; set; } = "mrs-postgres-primary";
    public int PrimaryHostPort { get; set; } = 5432;
    public int ReplicaHostPortRangeStart { get; set; } = 5433;
    public int InternalPostgresPort { get; set; } = 5432;
    public int ReconcileIntervalSeconds { get; set; } = 10;
}
