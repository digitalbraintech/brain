using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Storage;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public sealed class BrainSimulationOptions
{
    public required ModuleManifest Modules { get; init; }
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public string? PersistenceDirectory { get; init; }
    public IReadOnlyDictionary<string, string?>? Configuration { get; init; }
    public bool UseExternalGateway { get; init; }
}

public sealed class BrainSimulation : IAsyncDisposable
{
    private const string TokenKey = "DigitalBrain:Testing:SimulationToken";
    private static readonly ConcurrentDictionary<string, BrainSimulationOptions> Configurations = new();
    private readonly InProcessTestCluster? inProcess;
    private readonly TestCluster? socketCluster;
    private readonly string? configurationToken;

    private BrainSimulation(InProcessTestCluster cluster)
    {
        inProcess = cluster;
        Grains = cluster.Client;
        Brain = DigitalBrainClient.Connect(cluster.Client, DigitalBrainNames.DefaultOwner);
    }

    private BrainSimulation(TestCluster cluster, string token)
    {
        socketCluster = cluster;
        configurationToken = token;
        Grains = cluster.Client;
        Brain = DigitalBrainClient.Connect(cluster.Client, DigitalBrainNames.DefaultOwner);
    }

    public IGrainFactory Grains { get; }
    public IDigitalBrain Brain { get; }

    public ExternalOrleansGateway ExternalGateway
    {
        get
        {
            var endpoint = SiloServices.GetRequiredService<IOptions<EndpointOptions>>().Value;
            var cluster = SiloServices.GetRequiredService<IOptions<ClusterOptions>>().Value;
            return new(endpoint.AdvertisedIPAddress.ToString(), endpoint.GatewayPort,
                cluster.ClusterId, cluster.ServiceId);
        }
    }

    public static async Task<BrainSimulation> StartAsync(BrainSimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.UseExternalGateway)
        {
            return await StartSocketAsync(options).ConfigureAwait(false);
        }

        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureHost(static host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        if (options.Configuration is { Count: > 0 } configuration)
        {
            builder.ConfigureHost(host => host.Configuration.AddInMemoryCollection(configuration));
        }
        builder.ConfigureSilo((_, silo) => ConfigureSilo(silo, options));
        builder.ConfigureClient(client => ModelPayloadSerialization.AddModelPayloadSerialization(client.Services));
        var cluster = builder.Build();
        await cluster.DeployAsync().ConfigureAwait(false);
        return new(cluster);
    }

    private static async Task<BrainSimulation> StartSocketAsync(BrainSimulationOptions options)
    {
        var token = Guid.NewGuid().ToString("N");
        Configurations[token] = options;
        try
        {
            var builder = new TestClusterBuilder(1);
            builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            builder.ConfigureHostConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(options.Configuration ?? new Dictionary<string, string?>());
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { [TokenKey] = token });
            });
            builder.AddSiloBuilderConfigurator<SocketSiloConfigurator>();
            builder.AddClientBuilderConfigurator<SocketClientConfigurator>();
            var cluster = builder.Build();
            await cluster.DeployAsync().ConfigureAwait(false);
            return new(cluster, token);
        }
        catch
        {
            Configurations.TryRemove(token, out _);
            throw;
        }
    }

    private static void ConfigureSilo(ISiloBuilder silo, BrainSimulationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PersistenceDirectory))
        {
            silo.Services.AddSingleton<IJournalStorageProvider>(new FileJournalStorageProvider(options.PersistenceDirectory));
        }
        else
        {
            silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        }
        DigitalBrainRuntime.Add(silo, options.Modules);
        if (!string.IsNullOrWhiteSpace(options.PersistenceDirectory))
        {
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage,
                (services, _) => new FileGrainStorage(options.PersistenceDirectory,
                    services.GetRequiredService<Orleans.Serialization.Serializer>()));
        }
        else
        {
            silo.AddMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage);
        }
        silo.UseInMemoryReminderService();
        options.ConfigureSilo?.Invoke(silo);
    }

    public IDigitalBrain BrainFor(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return DigitalBrainClient.Connect(Grains, owner);
    }

    public string UniqueId(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var shortHex = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{shortHex}";
    }

    public T GetSiloService<T>() where T : notnull => SiloServices.GetRequiredService<T>();

    public async Task RestartSiloAsync(CancellationToken cancellationToken = default)
    {
        if (inProcess is not null)
        {
            var silo = inProcess.GetActiveSilos().Single();
            await inProcess.RestartSiloAsync(silo).WaitAsync(cancellationToken).ConfigureAwait(false);
            await inProcess.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        var socketSilo = socketCluster!.GetActiveSilos().Single();
        await socketCluster.RestartSiloAsync(socketSilo).WaitAsync(cancellationToken).ConfigureAwait(false);
        await socketCluster.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private IServiceProvider SiloServices => inProcess?.GetSiloServiceProvider()
        ?? socketCluster!.GetSiloServiceProvider();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (inProcess is not null) { await inProcess.DisposeAsync().ConfigureAwait(false); }
            if (socketCluster is not null) { await socketCluster.DisposeAsync().ConfigureAwait(false); }
        }
        finally
        {
            if (configurationToken is not null) { Configurations.TryRemove(configurationToken, out _); }
        }
    }

    public sealed class SocketSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var token = siloBuilder.Configuration[TokenKey]
                ?? throw new InvalidOperationException("The socket simulation configuration is missing.");
            ConfigureSilo(siloBuilder, Configurations[token]);
        }
    }

    public sealed class SocketClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            => ModelPayloadSerialization.AddModelPayloadSerialization(clientBuilder.Services);
    }
}
