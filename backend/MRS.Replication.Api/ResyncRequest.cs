namespace MRS.Replication.Api;

public sealed record ResyncRequest(string PrimaryHost, int PrimaryPort);
