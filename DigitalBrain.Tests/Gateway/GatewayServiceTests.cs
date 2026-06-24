using DigitalBrain.Protocol;
using DigitalBrain.Runtime.Grpc;
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

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<GatewaySiloConfig>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    private GatewayService NewService() =>
        new(_cluster.GrainFactory, new ConfigurationBuilder().Build(), new DigitalBrain.Silo.HomeFeedBus(), NullLogger<GatewayService>.Instance);

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

    private static ServerCallContext TestContext() => TestServerCallContext.Create();

    private sealed class GatewaySiloConfig : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryStreams("Default")
            .ConfigureServices(services =>
            {
                services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            });
    }
}
