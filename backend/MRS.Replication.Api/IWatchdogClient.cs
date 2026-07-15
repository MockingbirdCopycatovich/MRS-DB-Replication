using System.Net.Http.Json;
using System.Text.Json;
using MRS.Replication.Shared;

namespace MRS.Replication.Api;

public interface IWatchdogClient
{
    Task RegisterNodeAsync(RegisterNodeRequest request, CancellationToken ct);
    Task UnregisterNodeAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<NodeInfo>> GetStatusAsync(CancellationToken ct);
    Task<NodeInfo?> GetStatusAsync(string id, CancellationToken ct);
    Task<ReplicationConfig> GetConfigAsync(CancellationToken ct);
    Task UpdateConfigAsync(ReplicationConfig config, CancellationToken ct);
    IAsyncEnumerable<NodeEvent> StreamEventsAsync(CancellationToken ct);
}

public sealed class HttpWatchdogClient(HttpClient httpClient, ILogger<HttpWatchdogClient> logger) : IWatchdogClient
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.CreateOptions();

    public async Task RegisterNodeAsync(RegisterNodeRequest request, CancellationToken ct)
    {
        var response = await httpClient.PostAsJsonAsync("/nodes", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnregisterNodeAsync(string id, CancellationToken ct)
    {
        var response = await httpClient.DeleteAsync($"/nodes/{id}", ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<NodeInfo>> GetStatusAsync(CancellationToken ct)
    {
        var result = await httpClient.GetFromJsonAsync<List<NodeInfo>>("/status", JsonOptions, ct);
        return result ?? [];
    }

    public async Task<NodeInfo?> GetStatusAsync(string id, CancellationToken ct)
    {
        var response = await httpClient.GetAsync($"/status/{id}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<NodeInfo>(JsonOptions, ct);
    }

    public async Task<ReplicationConfig> GetConfigAsync(CancellationToken ct)
    {
        var result = await httpClient.GetFromJsonAsync<ReplicationConfig>("/config", JsonOptions, ct);
        return result ?? new ReplicationConfig();
    }

    public async Task UpdateConfigAsync(ReplicationConfig config, CancellationToken ct)
    {
        var response = await httpClient.PutAsJsonAsync("/config", config, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async IAsyncEnumerable<NodeEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await httpClient.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                yield break; // stream closed
            }
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            NodeEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<NodeEvent>(line["data: ".Length..], JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse SSE event: {Line}", line);
            }

            if (evt is not null)
            {
                yield return evt;
            }
        }
    }
}
