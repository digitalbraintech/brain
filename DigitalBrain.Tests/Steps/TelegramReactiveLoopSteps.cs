using System.Threading.Channels;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Tests.Kernel;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.TestingHost;
using Reqnroll;

namespace DigitalBrain.Tests.Steps;

// End-to-end proof of the full Telegram reactive loop over a real TestCluster, LLM stubbed:
// Send("TelegramMessageReceived") -> embodied responder pack -> AskLlm (broadcast) ->
// LlmResponderNeuron (fake IChatClient -> "ANSWER:hi") -> Signal("TelegramReplyRequested") -> egress bus.
[Binding]
[Collection("telegram-reactive-loop-host")]
public sealed class TelegramReactiveLoopSteps : IAsyncDisposable
{
    // Shared so the in-cluster grains and the out-of-cluster GatewayService read/write the same backing store.
    private static IPackConfigStore SharedConfigStore = null!;

    private readonly TestCluster _cluster;
    private readonly SignalEgressBus _egressBus;
    private const string PackName = "TelegramResponderNeuron";
    private const string Scope = "telegram-loop-user";

    private SignalEgressBus.Subscription? _egressSubscription;

    public TelegramReactiveLoopSteps()
    {
        var configServices = new ServiceCollection();
        configServices.AddDataProtection().UseEphemeralDataProtectionProvider();
        configServices.AddSingleton<IPackConfigBackingStore>(new InMemoryPackConfigBackingStore());
        configServices.AddSingleton<IPackConfigStore, PackConfigStore>();
        SharedConfigStore = configServices.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        _egressBus = new SignalEgressBus();
        TelegramReactiveLoopSiloConfig.SharedEgressBus = _egressBus;

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TelegramReactiveLoopSiloConfig>();
        _cluster = builder.Build();
        _cluster.DeployAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _egressSubscription?.Dispose();
        await _cluster.StopAllSilosAsync();
    }

    [Given(@"the Telegram responder experience is installed")]
    public async Task GivenTheTelegramResponderExperienceIsInstalled()
    {
        var market = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-telegram-loop");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0", Code: MarketplaceSeeds.TelegramResponderPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0", BuyerId: Scope));
    }

    [Then(@"the install emits a config form whose tree contains the fields ""(.*)"", ""(.*)"", ""(.*)""")]
    public async Task ThenTheInstallEmitsAConfigForm(string field1, string field2, string field3)
    {
        var gen = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());

        UiSurface? form = null;
        for (var attempt = 0; attempt < 40 && form is null; attempt++)
        {
            var timeline = await gen.GetTimelineAsync();
            form = timeline.OfType<UiSurface>().FirstOrDefault(s => s.Kind == ConfigFormSurface.Kind);
            if (form is null) await Task.Delay(50);
        }

        Assert.NotNull(form);
        var tree = Assert.IsType<UiWidgetTree>(form!.Props["tree"]);
        var keys = CollectFieldKeys(tree);
        Assert.Contains(field1, keys);
        Assert.Contains(field2, keys);
        Assert.Contains(field3, keys);
    }

    [When(@"I provide the Telegram configuration token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
    public async Task WhenIProvideTheTelegramConfiguration(string token, string provider, string key)
    {
        var values = new Dictionary<string, string>
        {
            ["telegram_token"] = token,
            ["llm_provider"] = provider,
            ["llm_key"] = key,
            ["pack"] = PackName,
            ["scope"] = Scope
        };
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values);

        var gateway = new GatewayService(
            _cluster.GrainFactory,
            new ConfigurationBuilder().Build(),
            new HomeFeedBus(),
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance,
            SharedConfigStore);

        await gateway.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        var stored = await SharedConfigStore.GetAsync(Scope, PackName);
        Assert.Equal(token, stored["telegram_token"]);
    }

    [When(@"the LLM responder is active and the egress bus is watching ""(.*)""")]
    public async Task WhenTheResponderIsActiveAndEgressIsWatching(string replyType)
    {
        // Activate the responder so it subscribes to the timeline before the AskLlm broadcast arrives.
        // Production will need a startup activation of LlmResponderNeuron (slice-5 / Program.cs concern).
        var responder = _cluster.GrainFactory.GetGrain<ILlmResponderNeuron>("telegram-loop-responder");
        await responder.GetTimelineAsync();

        _egressSubscription = _egressBus.Subscribe(new[] { replyType });
    }

    [When(@"a Telegram message arrives for chat (\d+) with text ""(.*)""")]
    public async Task WhenATelegramMessageArrives(int chatId, string text)
    {
        // Mirrors the generic Send -> IngressNeuron path: broadcast a named Signal on the timeline.
        var ingress = _cluster.GrainFactory.GetGrain<IIngressNeuron>("telegram-loop-ingress");
        await ingress.IngestAsync("TelegramMessageReceived",
            new Dictionary<string, object?> { ["chatId"] = chatId, ["text"] = text });
    }

    [Then(@"the embodied pack emits an AskLlm for ""(.*)""")]
    public async Task ThenTheEmbodiedPackEmitsAnAskLlm(string prompt)
    {
        var gen = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());

        AskLlm? ask = null;
        for (var attempt = 0; attempt < 40 && ask is null; attempt++)
        {
            var timeline = await gen.GetTimelineAsync();
            ask = timeline.OfType<AskLlm>().FirstOrDefault(a => a.Prompt == prompt);
            if (ask is null) await Task.Delay(50);
        }

        Assert.NotNull(ask);
        Assert.Equal("TelegramReplyRequested", ask!.ReplyType);
    }

    [Then(@"a ""(.*)"" reply for chat (\d+) with text ""(.*)"" reaches the egress bus")]
    public async Task ThenAReplyReachesTheEgressBus(string replyType, int chatId, string text)
    {
        Assert.NotNull(_egressSubscription);

        Signal? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (received is null)
            {
                var signal = await _egressSubscription!.Reader.ReadAsync(cts.Token);
                if (signal.Name == replyType) received = signal;
            }
        }
        catch (OperationCanceledException)
        {
            // received stays null -> the assertion below fails with a clear message.
        }

        Assert.NotNull(received);
        Assert.Equal(replyType, received!.Name);
        Assert.True(received.Props.TryGetValue("chatId", out var rawChatId), "Reply must carry chatId");
        Assert.True(Convert.ToInt32(rawChatId) == chatId, $"chatId should be {chatId}, was {rawChatId}");
        Assert.Equal(text, received.Props["text"]);
    }

    private static IReadOnlyList<string> CollectFieldKeys(UiWidgetTree tree) =>
        FindNodes(tree)
            .Where(n => n.Type == DigitalBrain.Core.Ui.TextField || n.Type == DigitalBrain.Core.Ui.Select)
            .Select(n => n.Props.GetValueOrDefault("key")?.ToString() ?? n.Props.GetValueOrDefault("name")?.ToString())
            .OfType<string>()
            .ToList();

    private static IEnumerable<UiWidgetTree> FindNodes(UiWidgetTree node)
    {
        yield return node;
        if (node.Children is null) yield break;
        foreach (var child in node.Children)
            foreach (var descendant in FindNodes(child))
                yield return descendant;
    }

    private sealed class TelegramReactiveLoopSiloConfig : ISiloConfigurator
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
                services.AddSingleton<HomeFeedBus>();
                services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
                services.AddSingleton(SharedEgressBus);
                services.AddSignalEgressStreamSubscriber();
                services.AddSingleton(SharedConfigStore);
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false"
                        })
                        .Build());
            });
    }
}

[CollectionDefinition("telegram-reactive-loop-host", DisableParallelization = true)]
public sealed class TelegramReactiveLoopHostCollection;

// N+1 reactivity proof: two packs (TelegramResponderNeuron + KeywordWatcherNeuron) both react to ONE
// TelegramMessageReceived broadcast — proving N+1 handler count with no silo restart.
[Binding]
[Collection("telegram-n1-reactivity-host")]
public sealed class TelegramN1ReactivitySteps : IAsyncDisposable
{
    private static IPackConfigStore SharedN1ConfigStore = null!;

    private readonly TestCluster _cluster;
    private readonly SignalEgressBus _egressBus;
    private const string ResponderPackName = "TelegramResponderNeuron";
    private const string WatcherPackName   = "KeywordWatcherNeuron";
    private const string N1Scope           = "n1-reactivity-user";

    // Signals collected from the egress bus in arrival order; both Then-steps read from this list.
    private readonly List<Signal> _collectedSignals = new();
    private SignalEgressBus.Subscription? _egressSubscription;

    public TelegramN1ReactivitySteps()
    {
        var configServices = new ServiceCollection();
        configServices.AddDataProtection().UseEphemeralDataProtectionProvider();
        configServices.AddSingleton<IPackConfigBackingStore>(new InMemoryPackConfigBackingStore());
        configServices.AddSingleton<IPackConfigStore, PackConfigStore>();
        SharedN1ConfigStore = configServices.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        _egressBus = new SignalEgressBus();
        TelegramN1SiloConfig.SharedEgressBus = _egressBus;

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TelegramN1SiloConfig>();
        _cluster = builder.Build();
        _cluster.DeployAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _egressSubscription?.Dispose();
        await _cluster.StopAllSilosAsync();
    }

    [Given(@"I provide the Telegram configuration token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
    public async Task GivenN1TelegramConfig(string token, string provider, string key)
    {
        var values = new Dictionary<string, string>
        {
            ["telegram_token"] = token,
            ["llm_provider"]   = provider,
            ["llm_key"]        = key,
            ["pack"]           = ResponderPackName,
            ["scope"]          = N1Scope
        };
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values);

        var gateway = new GatewayService(
            _cluster.GrainFactory,
            new ConfigurationBuilder().Build(),
            new HomeFeedBus(),
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance,
            SharedN1ConfigStore);

        await gateway.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            Payload  = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());
    }

    [Given(@"both the Telegram responder and the keyword watcher are installed")]
    public async Task GivenBothPacksAreInstalled()
    {
        var market = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-n1-proof");

        await market.FireAsync(new PublishToMarketplace(
            ResponderPackName, "1.0", Code: MarketplaceSeeds.TelegramResponderPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(ResponderPackName, "1.0", BuyerId: N1Scope));

        await market.FireAsync(new PublishToMarketplace(
            WatcherPackName, "1.0", Code: MarketplaceSeeds.KeywordWatcherPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(WatcherPackName, "1.0", BuyerId: N1Scope));
    }

    [Given(@"the LLM responder is active and the egress bus is watching ""(.*)"" and ""(.*)""")]
    public async Task GivenResponderActiveAndEgressWatchingTwo(string type1, string type2)
    {
        var responder = _cluster.GrainFactory.GetGrain<ILlmResponderNeuron>("n1-llm-responder");
        await responder.GetTimelineAsync();

        _egressSubscription = _egressBus.Subscribe(new[] { type1, type2 });

        // Drain the channel into _collectedSignals so both Then-steps can assert without consuming each other's signals.
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var signal in _egressSubscription.Reader.ReadAllAsync())
                    lock (_collectedSignals) _collectedSignals.Add(signal);
            }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
        });
    }

    [When(@"a Telegram message with text ""(.*)"" is ingested for chat (\d+)")]
    public async Task WhenATelegramMessageIsIngestedForChat(string text, int chatId)
    {
        var ingress = _cluster.GrainFactory.GetGrain<IIngressNeuron>("n1-ingress");
        await ingress.IngestAsync("TelegramMessageReceived",
            new Dictionary<string, object?> { ["chatId"] = chatId, ["text"] = text });
    }

    [Then(@"a ""(.*)"" reply for chat (\d+) reaches the egress bus")]
    public async Task ThenAReplyForChatReachesEgressBus(string signalName, int chatId)
    {
        var received = await WaitForSignalAsync(signalName, chatId);

        Assert.NotNull(received);
        Assert.Equal(signalName, received!.Name);
        Assert.Equal(chatId, Convert.ToInt32(received.Props.GetValueOrDefault("chatId")));

        if (signalName == "TelegramReplyRequested")
        {
            var text = received.Props.GetValueOrDefault("text")?.ToString() ?? "";
            Assert.StartsWith("ANSWER:", text);
        }
    }

    [Then(@"a ""(.*)"" signal for chat (\d+) reaches the egress bus")]
    public async Task ThenAReminderSignalForChatReachesEgressBus(string signalName, int chatId)
    {
        var received = await WaitForSignalAsync(signalName, chatId);

        Assert.NotNull(received);
        Assert.Equal(signalName, received!.Name);
        Assert.Equal(chatId, Convert.ToInt32(received.Props.GetValueOrDefault("chatId")));
        var reminder = received.Props.GetValueOrDefault("reminder")?.ToString() ?? "";
        Assert.Contains("remind me", reminder, StringComparison.OrdinalIgnoreCase);
    }

    // Polls _collectedSignals (fed by a background pump) so both assertions read from the same captured list
    // without consuming each other's signals.
    private async Task<Signal?> WaitForSignalAsync(string name, int chatId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (_collectedSignals)
            {
                var match = _collectedSignals.FirstOrDefault(s =>
                    s.Name == name && Convert.ToInt32(s.Props.GetValueOrDefault("chatId")) == chatId);
                if (match is not null) return match;
            }
            await Task.Delay(50);
        }
        return null;
    }

    private sealed class TelegramN1SiloConfig : ISiloConfigurator
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
                services.AddSingleton<HomeFeedBus>();
                services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
                services.AddSingleton(SharedEgressBus);
                services.AddSignalEgressStreamSubscriber();
                services.AddSingleton(SharedN1ConfigStore);
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false"
                        })
                        .Build());
            });
    }
}

[CollectionDefinition("telegram-n1-reactivity-host", DisableParallelization = true)]
public sealed class TelegramN1ReactivityHostCollection;
