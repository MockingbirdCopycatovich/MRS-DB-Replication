using MRS.Replication.Shared;

namespace MRS.Replication.Api;

/// <summary>Api-side read replica of Watchdog's authoritative ReplicationConfig, refreshed on every change made through this API and periodically in the background.</summary>
public sealed class ReplicationConfigCache(IWatchdogClient watchdogClient, ILogger<ReplicationConfigCache> logger) : BackgroundService
{
    private ReplicationConfig _current = new();

    public ReplicationConfig Current => _current;

    public void Set(ReplicationConfig config) => _current = config;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _current = await watchdogClient.GetConfigAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not refresh replication config from Watchdog");
            }

            await Task.Delay(10_000, stoppingToken);
        }
    }
}
