using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using Grpc.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Gateway;

// Task 7b store-and-PULL: a submitted config persists encrypted, GetPackConfig returns the DECRYPTED
// values point-to-point, and the broadcast carries only a NON-SECRET PackConfigured notification — the
// token never reaches the egress timeline. Mirrors WatchSynapsesTests' egress wiring.
[Collection("pack-config-pull-host")]
public class PackConfigPullTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private SignalEgressBus _egressBus = null!;
    private IPackConfigStore _configStore = null!;

    public async Task InitializeAsync()
    {
        _egressBus = new SignalEgressBus();
        PackConfigPullSiloConfig.SharedEgressBus = _egressBus;

        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IPackConfigBackingStore, InMemoryPackConfigBackingStore>();
        services.AddSingleton<IPackConfigStore, PackConfigStore>();
        _configStore = services.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<PackConfigPullSiloConfig>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    private GatewayService NewService() =>
        new(_cluster.GrainFactory, new ConfigurationBuilder().Build(), new HomeFeedBus(),
            _egressBus, new FakeHostEnvironment(), NullLogger<GatewayService>.Instance, _configStore);

    // A production-equivalent service whose GetPackConfig gate is armed with a configured InternalServiceKey.
    private GatewayService NewGatedService(string internalKey) =>
        new(_cluster.GrainFactory,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:InternalServiceKey"] = internalKey })
                .Build(),
            new HomeFeedBus(), _egressBus, new FakeHostEnvironment("Production"),
            NullLogger<GatewayService>.Instance, _configStore);

    private async Task StoreConfigAsync(GatewayService svc)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"scope\":\"default\",\"telegram_token\":\"123:ABC\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-auth",
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());
    }

    [Fact]
    public async Task GetPackConfig_ReturnsStoredValues_AfterConfigurationProvided()
    {
        var svc = NewService();
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"scope\":\"default\",\"telegram_token\":\"123:ABC\",\"llm_provider\":\"ollama\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-1",
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        var reply = await svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.Create());

        Assert.Equal("123:ABC", reply.Values["telegram_token"]);
        Assert.Equal("ollama", reply.Values["llm_provider"]);
    }

    // Regression: a form submitted with a sessionId (but no explicit scope) must still land under "default" —
    // the scope every reader (responder pack, LlmResponderNeuron, transport) actually pulls from. Deriving the
    // scope from sessionId would strand the token where no reader looks.
    [Fact]
    public async Task ConfigurationProvided_WithSessionIdButNoScope_StoredUnderDefaultScope()
    {
        var svc = NewService();
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"sessionId\":\"user-session-42\",\"telegram_token\":\"123:ABC\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-session",
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        var reply = await svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.Create());

        Assert.Equal("123:ABC", reply.Values["telegram_token"]);
        Assert.False(reply.Values.ContainsKey("sessionId"), "sessionId is a control key, not a stored config value.");
    }

    [Fact]
    public async Task ConfigurationProvided_BroadcastsNonSecretPackConfigured_WithoutToken()
    {
        using var subscription = _egressBus.Subscribe(new[] { "PackConfigured" });
        var svc = NewService();

        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"scope\":\"default\",\"telegram_token\":\"123:ABC\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-2",
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        Signal? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { received = await subscription.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) { }

        Assert.NotNull(received);
        Assert.Equal("PackConfigured", received!.Name);
        Assert.Equal("TelegramResponderNeuron", received.Props["pack"]);
        Assert.Equal("default", received.Props["scope"]);
        Assert.False(received.Props.ContainsKey("telegram_token"),
            "The secret token must NEVER reach the broadcast egress.");
    }

    // SECURITY (the hole this task closes): GetPackConfig sits on the SAME external gRPC service a browser reaches.
    // A caller that does NOT present the shared internal service key (i.e. any untrusted internet/browser client)
    // must be REJECTED before any decrypted secret is returned.
    [Fact]
    public async Task GetPackConfig_WithoutInternalKey_IsRejectedUnauthenticated()
    {
        const string internalKey = "super-secret-internal-key";
        var svc = NewGatedService(internalKey);
        await StoreConfigAsync(svc);

        // No x-internal-key header — a browser/untrusted caller.
        var noHeaderEx = await Assert.ThrowsAsync<RpcException>(() => svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.Create()));
        Assert.Equal(StatusCode.Unauthenticated, noHeaderEx.StatusCode);

        // Wrong key — still rejected.
        var wrongKeyEx = await Assert.ThrowsAsync<RpcException>(() => svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.WithHeaders(("x-internal-key", "not-the-key"))));
        Assert.Equal(StatusCode.Unauthenticated, wrongKeyEx.StatusCode);
    }

    [Fact]
    public async Task GetPackConfig_WithCorrectInternalKey_ReturnsValues()
    {
        const string internalKey = "super-secret-internal-key";
        var svc = NewGatedService(internalKey);
        await StoreConfigAsync(svc);

        var reply = await svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.WithHeaders(("x-internal-key", internalKey)));

        Assert.Equal("123:ABC", reply.Values["telegram_token"]);
    }

    private sealed class PackConfigPullSiloConfig : ISiloConfigurator
    {
        public static SignalEgressBus SharedEgressBus { get; set; } = new();

        public void Configure(ISiloBuilder siloBuilder) => siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryStreams("Default")
            .AddMemoryStreams("HomeFeed")
            .AddMemoryStreams("DigitalBrainTimeline")
            .AddMemoryGrainStorage("PubSubStore")
            .ConfigureServices(services =>
            {
                services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddScoped<NeuronJournals>();
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
                services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
                services.AddSingleton(SharedEgressBus);
                services.AddSignalEgressStreamSubscriber();
            });
    }
}

[CollectionDefinition("pack-config-pull-host", DisableParallelization = true)]
public sealed class PackConfigPullHostCollection;
