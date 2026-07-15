using MRS.Replication.Shared;
using MRS.Replication.Watchdog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<NodeRegistry>();
builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<INodeProbe, NpgsqlNodeProbe>();
builder.Services.AddSingleton<FailoverService>();
builder.Services.AddSingleton<ResyncService>();
builder.Services.AddHostedService<NodeMonitorService>();

builder.Services.AddHttpClient<IReplicaManagerClient, HttpReplicaManagerClient>(client =>
{
    var backendBaseUrl = builder.Configuration["Backend:BaseUrl"] ?? "http://backend:8080";
    client.BaseAddress = new Uri(backendBaseUrl);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseCors();

// Always on (not gated by ASPNETCORE_ENVIRONMENT): this is a local-demo tool, and the
// docker-compose deployment runs in the default "Production" environment, so gating this
// behind IsDevelopment() would make it unreachable from the browser at http://localhost:5081/swagger.
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/nodes", (RegisterNodeRequest request, NodeRegistry registry, EventBus bus) =>
{
    var node = registry.Register(request);
    bus.Publish(new NodeEvent { NodeId = node.Id, Type = NodeEventType.Registered, Message = $"{node.Name} registered as {node.Role}", Node = node });
    return Results.Ok(node);
});

app.MapDelete("/nodes/{id}", (string id, NodeRegistry registry, EventBus bus) =>
{
    var node = registry.Get(id);
    if (!registry.Remove(id))
    {
        return Results.NotFound();
    }
    bus.Publish(new NodeEvent { NodeId = id, Type = NodeEventType.Removed, Message = $"{node?.Name ?? id} unregistered" });
    return Results.NoContent();
});

app.MapGet("/status", (NodeRegistry registry) => Results.Ok(registry.GetAll()));

app.MapGet("/status/{id}", (string id, NodeRegistry registry) =>
{
    var node = registry.Get(id);
    return node is null ? Results.NotFound() : Results.Ok(node);
});

app.MapGet("/config", (ConfigStore configStore) => Results.Ok(configStore.Current));

app.MapPut("/config", (ReplicationConfig config, ConfigStore configStore) =>
{
    configStore.Update(config);
    return Results.Ok(configStore.Current);
});

var sseJsonOptions = JsonDefaults.CreateOptions();

app.MapGet("/events", async (HttpContext context, EventBus bus, CancellationToken ct) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    var subscription = bus.Subscribe();
    try
    {
        await context.Response.WriteAsync(": connected\n\n", ct);
        await context.Response.Body.FlushAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var evt = await subscription.Reader.ReadAsync(ct);
            var json = System.Text.Json.JsonSerializer.Serialize(evt, sseJsonOptions);
            await context.Response.WriteAsync($"data: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // client disconnected
    }
    finally
    {
        subscription.Handle.Dispose();
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
