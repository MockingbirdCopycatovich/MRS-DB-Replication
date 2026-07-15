using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace MRS.Replication.Api;

public sealed class DockerContainerOrchestrator : IContainerOrchestrator, IDisposable
{
    private readonly DockerOptions _options;
    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerOrchestrator> _logger;

    public DockerContainerOrchestrator(IOptions<DockerOptions> options, ILogger<DockerContainerOrchestrator> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new DockerClientConfiguration(new Uri(_options.SocketPath)).CreateClient();
    }

    public async Task EnsurePrimaryAsync(PrimarySpec spec, CancellationToken ct)
    {
        if (await ExistsAsync(spec.ContainerName, ct))
        {
            return;
        }

        var env = new List<string>
        {
            "MRS_ROLE=primary",
            $"MRS_NODE_ID=primary",
            $"POSTGRES_USER={spec.User}",
            $"POSTGRES_PASSWORD={spec.Password}",
            $"POSTGRES_DB={spec.Db}"
        };

        await CreateAndStartAsync(spec.ContainerName, spec.HostPort, env, role: "primary", nodeId: "primary", ct);
    }

    public async Task EnsureReplicaAsync(ReplicaSpec spec, PrimarySpec primary, CancellationToken ct)
    {
        if (await ExistsAsync(spec.ContainerName, ct))
        {
            return;
        }

        var env = new List<string>
        {
            "MRS_ROLE=replica",
            $"MRS_NODE_ID={spec.Id}",
            $"PRIMARY_HOST={primary.ContainerName}",
            $"PRIMARY_PORT={_options.InternalPostgresPort}",
            $"POSTGRES_USER={primary.User}",
            $"POSTGRES_PASSWORD={primary.Password}",
            $"POSTGRES_DB={primary.Db}"
        };

        await CreateAndStartAsync(spec.ContainerName, spec.HostPort, env, role: "replica", nodeId: spec.Id, ct);
    }

    private async Task CreateAndStartAsync(string containerName, int hostPort, List<string> env, string role, string nodeId, CancellationToken ct)
    {
        var portKey = $"{_options.InternalPostgresPort}/tcp";

        var createResponse = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = _options.Image,
            Name = containerName,
            Env = env,
            Labels = new Dictionary<string, string>
            {
                [_options.StackLabel] = _options.StackLabelValue,
                ["mrs.role"] = role,
                ["mrs.node-id"] = nodeId
            },
            ExposedPorts = new Dictionary<string, EmptyStruct> { [portKey] = default },
            HostConfig = new HostConfig
            {
                NetworkMode = _options.NetworkName,
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    [portKey] = [new PortBinding { HostPort = hostPort.ToString() }]
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            }
        }, ct);

        await _client.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters(), ct);
        _logger.LogInformation("Started container {Container} (role={Role})", containerName, role);
    }

    public async Task<bool> ExistsAsync(string containerName, CancellationToken ct)
    {
        var list = await ListAllAsync(ct);
        return list.Any(c => c.Names.Contains("/" + containerName));
    }

    public async Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken ct)
    {
        var list = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{_options.StackLabel}={_options.StackLabelValue}"] = true }
            }
        }, ct);

        return list.Select(c => new ManagedContainer(
            c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID,
            c.ID,
            c.State,
            (IReadOnlyDictionary<string, string>)(c.Labels ?? new Dictionary<string, string>()))).ToArray();
    }

    private async Task<IList<ContainerListResponse>> ListAllAsync(CancellationToken ct) =>
        await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);

    public async Task StartAsync(string containerName, CancellationToken ct) =>
        await _client.Containers.StartContainerAsync(containerName, new ContainerStartParameters(), ct);

    public async Task RemoveAsync(string containerName, CancellationToken ct)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(containerName, new ContainerRemoveParameters { Force = true }, ct);
        }
        catch (DockerContainerNotFoundException)
        {
            // already gone
        }
    }

    public void Dispose() => _client.Dispose();
}
