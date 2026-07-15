using System.Net.Http.Json;

namespace MRS.Replication.Watchdog;

/// <summary>Watchdog has no Docker access itself (that stays in the Backend's Replica Manager) — it just asks the Backend to act on a container.</summary>
public interface IReplicaManagerClient
{
    Task<bool> TriggerResyncAsync(string nodeId, string primaryHost, int primaryPort, CancellationToken ct);

    /// <summary>Used only when a node resumes as Primary with no failover having occurred — just needs its container (re)started, not a full resync-as-replica.</summary>
    Task<bool> StartNodeAsync(string nodeId, CancellationToken ct);
}

public sealed class HttpReplicaManagerClient(HttpClient httpClient, ILogger<HttpReplicaManagerClient> logger) : IReplicaManagerClient
{
    public async Task<bool> TriggerResyncAsync(string nodeId, string primaryHost, int primaryPort, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"/api/replicas/{nodeId}/resync",
                new { primaryHost, primaryPort },
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Resync trigger for {NodeId} returned {Status}", nodeId, response.StatusCode);
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resync trigger for {NodeId} failed", nodeId);
            return false;
        }
    }

    public async Task<bool> StartNodeAsync(string nodeId, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.PostAsync($"/api/nodes/{nodeId}/start", content: null, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Start request for {NodeId} returned {Status}", nodeId, response.StatusCode);
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Start request for {NodeId} failed", nodeId);
            return false;
        }
    }
}
