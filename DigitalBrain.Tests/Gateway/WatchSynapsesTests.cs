using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Gateway;

// Proves the OUTBOUND mirror of generic Send: a broadcast Signal travels the DigitalBrainTimeline stream,
// the per-silo SignalEgressStreamSubscriber forwards it into SignalEgressBus, and a filtered subscription
// (the mechanism WatchSynapses streams to external transports) yields only the matching signal.
[Collection("signal-egress-host")]
public class WatchSynapsesTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private SignalEgressBus _egressBus = null!;

    public async Task InitializeAsync()
    {
        _egressBus = new SignalEgressBus();
        SignalEgressSiloConfig.SharedEgressBus = _egressBus;
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SignalEgressSiloConfig>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    [Fact]
    public async Task BroadcastSignal_ReachesEgressBus_FilteredByTypeName()
    {
        using var subscription = _egressBus.Subscribe(new[] { TelegramSignals.ReplyRequested });

        var emitter = _cluster.GrainFactory.GetGrain<IIngressNeuron>("egress-emitter-1");
        await emitter.IngestAsync(TelegramSignals.ReplyRequested,
            new Dictionary<string, object?> { ["chatId"] = 7L, ["text"] = "yo" });
        await emitter.IngestAsync("Other", new Dictionary<string, object?>());

        Signal? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            received = await subscription.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // received stays null -> assertion below fails with a clear message
        }

        Assert.NotNull(received);
        Assert.Equal(TelegramSignals.ReplyRequested, received!.Name);
        Assert.True(received.Props.TryGetValue("chatId", out var chatId), "Props should contain 'chatId'");
        Assert.True(chatId is 7 or 7L, $"chatId should be 7 (numeric), was {chatId} ({chatId?.GetType().Name})");
        Assert.Equal("yo", received.Props["text"]);

        // The non-matching "Other" signal must never be delivered to a filtered subscriber.
        Assert.False(subscription.Reader.TryRead(out _), "Only TelegramReplyRequested should reach a filtered subscription");
    }

    private sealed class SignalEgressSiloConfig : ISiloConfigurator
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

[CollectionDefinition("signal-egress-host", DisableParallelization = true)]
public sealed class SignalEgressHostCollection;
