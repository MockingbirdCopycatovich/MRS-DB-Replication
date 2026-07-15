using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MRS.Replication.Shared;

namespace MRS.Replication.Api;

/// <summary>
/// Kubernetes-lite reconciliation loop (spec §3.2): diffs DesiredStateStore against what
/// Docker actually reports, creates/removes/restarts containers accordingly, and keeps
/// Watchdog's node registry in sync. Also keeps the sync/async standby list up to date as
/// replicas are added or removed.
/// </summary>
public sealed class ReconciliationService(
    DesiredStateStore desired,
    IContainerOrchestrator orchestrator,
    IWatchdogClient watchdogClient,
    ReplicationConfigCache configCache,
    PrimaryAdminService primaryAdmin,
    IOptions<DockerOptions> dockerOptions,
    ILogger<ReconciliationService> logger) : BackgroundService
{
    private readonly DockerOptions _options = dockerOptions.Value;
    private readonly ConcurrentDictionary<string, bool> _registered = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reconciliation cycle failed");
            }

            await Task.Delay(_options.ReconcileIntervalSeconds * 1000, stoppingToken);
        }
    }

    /// <summary>Internal (not private) so tests can drive a single reconciliation pass without running the BackgroundService loop.</summary>
    internal async Task ReconcileAsync(CancellationToken ct)
    {
        var primary = desired.Primary;
        if (primary is null)
        {
            return; // setup wizard hasn't run yet
        }

        await orchestrator.EnsurePrimaryAsync(primary, ct);
        await RegisterOnceAsync("primary", primary.ContainerName, _options.InternalPostgresPort, NodeRole.Primary, ct);

        var replicas = desired.GetReplicas();
        foreach (var replica in replicas)
        {
            await orchestrator.EnsureReplicaAsync(replica, primary, ct);
            await RegisterOnceAsync(replica.Id, replica.ContainerName, _options.InternalPostgresPort, NodeRole.Replica, ct);
        }

        await RemoveExcessContainersAsync(primary, replicas, ct);
        await RestartUnexpectedlyStoppedAsync(primary, replicas, ct);

        if (configCache.Current.Mode == ReplicationMode.Sync)
        {
            try
            {
                await primaryAdmin.ApplyReplicationModeAsync(primary, ReplicationMode.Sync, replicas.Select(r => r.Id), ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not (re)apply synchronous_standby_names — primary may not be ready yet");
            }
        }
    }

    private async Task RegisterOnceAsync(string id, string containerName, int port, NodeRole role, CancellationToken ct)
    {
        if (!_registered.TryAdd(id, true))
        {
            return;
        }

        try
        {
            await watchdogClient.RegisterNodeAsync(new RegisterNodeRequest
            {
                Id = id,
                Name = id,
                Role = role,
                Host = containerName,
                Port = port
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register node {NodeId} with Watchdog, will retry next cycle", id);
            _registered.TryRemove(id, out _);
        }
    }

    private async Task RemoveExcessContainersAsync(PrimarySpec primary, IReadOnlyList<ReplicaSpec> desiredReplicas, CancellationToken ct)
    {
        var actual = await orchestrator.ListManagedAsync(ct);
        var desiredNames = new HashSet<string>(desiredReplicas.Select(r => r.ContainerName)) { primary.ContainerName };

        foreach (var container in actual)
        {
            if (desiredNames.Contains(container.Name))
            {
                continue;
            }

            logger.LogInformation("Removing excess container {Container} (not in desired state)", container.Name);
            await orchestrator.RemoveAsync(container.Name, ct);

            var id = container.Labels.GetValueOrDefault("mrs.node-id") ?? desiredReplicas.FirstOrDefault(r => r.ContainerName == container.Name)?.Id;
            if (id is not null)
            {
                _registered.TryRemove(id, out _);
                try { await watchdogClient.UnregisterNodeAsync(id, ct); } catch { /* best effort */ }
            }
        }
    }

    private async Task RestartUnexpectedlyStoppedAsync(PrimarySpec primary, IReadOnlyList<ReplicaSpec> desiredReplicas, CancellationToken ct)
    {
        // Deliberately excludes the primary container: if it crashes, whether it should just
        // restart or hand off via failover is a Postgres-level decision that belongs to
        // Watchdog alone (see ResyncService's "resume as primary" / mandatory-resync paths).
        // Auto-restarting it here would race Watchdog's failure-threshold and could restart it
        // right as (or after) a replica gets promoted elsewhere, risking two primaries at once.
        var actual = await orchestrator.ListManagedAsync(ct);
        var desiredReplicaNames = new HashSet<string>(desiredReplicas.Select(r => r.ContainerName));

        foreach (var container in actual)
        {
            if (desiredReplicaNames.Contains(container.Name) && !container.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Replica container {Container} is {State}, restarting", container.Name, container.State);
                await orchestrator.StartAsync(container.Name, ct);
            }
        }
    }
}
