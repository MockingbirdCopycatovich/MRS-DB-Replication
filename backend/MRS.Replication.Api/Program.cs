using Microsoft.Extensions.Options;
using MRS.Replication.Api;
using MRS.Replication.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DockerOptions>(builder.Configuration.GetSection("Docker"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));

builder.Services.AddSingleton<DesiredStateStore>();
builder.Services.AddSingleton<IContainerOrchestrator, DockerContainerOrchestrator>();
builder.Services.AddSingleton<NodeQueueService>();
builder.Services.AddSingleton<ISqlExecutor, NpgsqlSqlExecutor>();
builder.Services.AddSingleton<QueryRouterService>();
builder.Services.AddSingleton<PrimaryAdminService>();

builder.Services.AddHttpClient<IWatchdogClient, HttpWatchdogClient>(client =>
{
    var watchdogBaseUrl = builder.Configuration["Watchdog:BaseUrl"] ?? "http://watchdog:8080";
    client.BaseAddress = new Uri(watchdogBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30); // long enough to keep the /events SSE connection open
});

builder.Services.AddSingleton<LiveNodeCache>();
builder.Services.AddSingleton<INodeCache>(sp => sp.GetRequiredService<LiveNodeCache>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveNodeCache>());

builder.Services.AddSingleton<ReplicationConfigCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReplicationConfigCache>());

builder.Services.AddHostedService<ReconciliationService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

// Always on (not gated by ASPNETCORE_ENVIRONMENT): this is a local-demo tool, and the
// docker-compose deployment runs in the default "Production" environment, so gating this
// behind IsDevelopment() would make it unreachable from the browser at http://localhost:5080/swagger.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.MapPost("/api/setup", async (SetupRequest request, DesiredStateStore desired, IWatchdogClient watchdog, IOptions<DockerOptions> dockerOptions, CancellationToken ct) =>
{
    var options = dockerOptions.Value;
    desired.SetPrimary(new PrimarySpec(options.PrimaryContainerName, options.PrimaryHostPort, request.PostgresUser, request.PostgresPassword, request.PostgresDb));
    desired.ScaleTo(request.ReplicaCount, options.ReplicaHostPortRangeStart);
    await watchdog.UpdateConfigAsync(request.Config, ct);
    return Results.Accepted(value: new { message = "Provisioning started", replicaCount = request.ReplicaCount });
});

app.MapPost("/api/replicas", (ReplicaCountRequest request, DesiredStateStore desired, IOptions<DockerOptions> dockerOptions) =>
{
    if (desired.Primary is null)
    {
        return Results.BadRequest(new { error = "Run POST /api/setup first" });
    }
    desired.ScaleTo(request.Count, dockerOptions.Value.ReplicaHostPortRangeStart);
    return Results.Ok(desired.GetReplicas());
});

app.MapDelete("/api/replicas/{id}", (string id, DesiredStateStore desired) =>
    desired.RemoveReplica(id) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/replicas", (DesiredStateStore desired) => Results.Ok(desired.GetReplicas()));

app.MapPost("/api/nodes/{id}/start", async (string id, DesiredStateStore desired, IContainerOrchestrator orchestrator, CancellationToken ct) =>
{
    if (desired.Primary is null)
    {
        return Results.BadRequest(new { error = "Run POST /api/setup first" });
    }

    var containerName = id == "primary"
        ? desired.Primary.ContainerName
        : desired.GetReplicas().FirstOrDefault(r => r.Id == id)?.ContainerName;

    if (containerName is null)
    {
        return Results.NotFound();
    }

    await orchestrator.StartAsync(containerName, ct);
    return Results.Ok(new { message = $"{containerName} start requested" });
});

app.MapPost("/api/replicas/{id}/resync", async (string id, ResyncRequest body, DesiredStateStore desired, IContainerOrchestrator orchestrator, CancellationToken ct) =>
{
    if (desired.Primary is null)
    {
        return Results.BadRequest(new { error = "Run POST /api/setup first" });
    }

    // Containers are stateless (no named volume for PGDATA), so "resync" = recreate the
    // container from scratch — this re-triggers pg_basebackup on first boot against the
    // (possibly new, post-failover) primary. Avoids the trap of running `pg_ctl stop` via
    // `docker exec` against a container whose PID 1 IS postgres, which would kill the container.
    string containerName;
    int hostPort;
    if (id == "primary")
    {
        containerName = desired.Primary.ContainerName;
        hostPort = desired.Primary.HostPort;
    }
    else
    {
        var spec = desired.GetReplicas().FirstOrDefault(r => r.Id == id);
        if (spec is null)
        {
            return Results.NotFound();
        }
        containerName = spec.ContainerName;
        hostPort = spec.HostPort;
    }

    await orchestrator.RemoveAsync(containerName, ct);

    var effectivePrimary = desired.Primary with { ContainerName = body.PrimaryHost };
    await orchestrator.EnsureReplicaAsync(new ReplicaSpec(id, containerName, hostPort), effectivePrimary, ct);

    return Results.Ok(new { message = $"{containerName} recreated as a fresh replica of {body.PrimaryHost}" });
});

app.MapPut("/api/config/mode", async (ChangeModeRequest request, DesiredStateStore desired, PrimaryAdminService primaryAdmin, IWatchdogClient watchdog, ReplicationConfigCache configCache, CancellationToken ct) =>
{
    if (desired.Primary is null)
    {
        return Results.BadRequest(new { error = "Run POST /api/setup first" });
    }

    var replicaIds = desired.GetReplicas().Select(r => r.Id).ToArray();
    await primaryAdmin.ApplyReplicationModeAsync(desired.Primary, request.Mode, replicaIds, ct);

    var newConfig = configCache.Current with
    {
        Mode = request.Mode,
        SyncTimeoutMs = request.SyncTimeoutMs ?? configCache.Current.SyncTimeoutMs
    };
    await watchdog.UpdateConfigAsync(newConfig, ct);
    configCache.Set(newConfig);

    return Results.Ok(newConfig);
});

app.MapGet("/api/nodes", (LiveNodeCache nodeCache, NodeQueueService queueService) =>
{
    var enriched = nodeCache.Snapshot().Select(n => n with { QueueDepth = queueService.DepthOf(n.Id) });
    return Results.Ok(enriched);
});

app.MapPost("/api/query", async (QueryRequest request, QueryRouterService router, CancellationToken ct) =>
    Results.Ok(await router.ExecuteAsync(request.Sql, ct)));

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
