using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Silo;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Silo.Gateway;
using DigitalBrain.Tests.TestSupport;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Gateway;

[Collection("silo-host")]
public class GatewayServiceTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private HomeFeedBus _homeFeedBus = null!;

    public async Task InitializeAsync()
    {
        _homeFeedBus = new HomeFeedBus();
        GatewaySiloConfig.SharedHomeFeedBus = _homeFeedBus;
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<GatewaySiloConfig>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    private GatewayService NewService() =>
        new(_cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus, NullLogger<GatewayService>.Instance);

    [Fact]
    public async Task Ask_Ino_ReturnsNonEmptyReply()
    {
        var reply = await NewService().Ask(new AskRequest { NeuronId = "ino-main", Prompt = "hello" }, TestContext());
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    [Fact]
    public async Task Ask_NonIno_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            NewService().Ask(new AskRequest { NeuronId = "demo-x", Prompt = "hi" }, TestContext()));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Fire_ThenTimeline_ShowsDemoMessage()
    {
        var svc = NewService();
        await svc.Fire(new FireRequest { NeuronId = "demo-fire", Text = "ping-123" }, TestContext());

        var timeline = await svc.Timeline(new TimelineRequest { NeuronId = "demo-fire", MaxEntries = 10 }, TestContext());
        Assert.Contains(timeline.Entries, e => e.Type == nameof(DemoMessageSynapse) && e.Text.Contains("ping-123"));
    }

    [Fact]
    public async Task Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface()
    {
        using var subscription = _homeFeedBus.Subscribe();
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = KernelSurfaceDemo.RequestType,
            CorrelationId = "ui-demo-test"
        }, TestContext());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var cards = new List<RfwCard>();
        while (cards.Count < 8 &&
               (!cards.Any(c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal)) ||
                !cards.Any(c => c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal))))
        {
            cards.Add(await subscription.Reader.ReadAsync(timeout.Token));
        }

        var graph = Assert.Single(cards, c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal));
        Assert.Equal("digitalbrain", graph.LibraryName);
        Assert.Equal("root", graph.RootWidget);
        Assert.Contains("\"kind\":\"activity-graph\"", graph.DataJson);
        Assert.Contains("\"edges\"", graph.DataJson);
        Assert.Contains("\"correlationId\":\"ui-demo-test\"", graph.DataJson);

        var card = Assert.Single(cards, c => c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal));
        Assert.Equal("digitalbrain", card.LibraryName);
        Assert.Equal("root", card.RootWidget);
        Assert.False(string.IsNullOrWhiteSpace(card.CorrelationId));
        Assert.Contains("\"source\"", card.DataJson);
        Assert.Contains("Embodied pack live", card.DataJson);

        var generated = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>(KernelSurfaceDemo.GeneratedNeuronKey);
        var timeline = await generated.GetOutgoingTimelineAsync();
        var emittedSurface = Assert.Single(timeline.OfType<UiSurface>(), surface =>
            surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var id) &&
            Equals(id, "surface-demo-pack"));
        Assert.Equal("ui-demo-test", emittedSurface.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(emittedSurface.CausationId));

        var observability = _cluster.GrainFactory.GetGrain<IObservabilityNeuron>(KernelSurfaceDemo.ObservabilityNeuronKey);
        var graphTimeline = await observability.GetOutgoingTimelineAsync();
        Assert.Contains(graphTimeline.OfType<UiSurface>(), surface =>
            surface.Kind == UiSurfaceKinds.ActivityGraph &&
            surface.CorrelationId == "ui-demo-test");
    }

    private static ServerCallContext TestContext() => TestServerCallContext.Create();

    private sealed class GatewaySiloConfig : ISiloConfigurator
    {
        public static HomeFeedBus SharedHomeFeedBus { get; set; } = new();

        public void Configure(ISiloBuilder siloBuilder) => siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryStreams("Default")
            .ConfigureServices(services =>
            {
                services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddScoped<NeuronJournals>();
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
                services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
                services.AddSingleton(SharedHomeFeedBus);
            });
    }
}

