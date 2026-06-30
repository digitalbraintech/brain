using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.TestingHost;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests.Steps;

[Binding]
public class ConfigFormSteps : IAsyncDisposable
{
    // Shared so the in-cluster grains and the out-of-cluster GatewayService write/read the SAME backing store.
    private static IPackConfigStore SharedConfigStore = null!;

    private readonly TestCluster _cluster;
    private string _packName = "";
    private const string Scope = "config-form-user";

    public ConfigFormSteps()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IPackConfigBackingStore>(new InMemoryPackConfigBackingStore());
        services.AddSingleton<IPackConfigStore, PackConfigStore>();
        SharedConfigStore = services.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ConfigFormSiloConfig>();
        _cluster = builder.Build();
        _cluster.DeployAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync() => await _cluster.StopAllSilosAsync();

    // A domain-neutral pack with three RequiredConfig fields: a Text, a Choice, and a Secret.
    private static string ConfiguredPackSource(string packName)
    {
        const string q = "\"";
        return """
            using System.Collections.Generic;
            using DigitalBrain.Core;

            public sealed class GenericConfigured : IPackBehavior
            {
                public string Respond(string input) => "ok:" + (input ?? string.Empty);

                public PackManifest GetManifest() =>
                    new PackManifest(
                        new[] { new SynapseType(
            """ + q + "ExperienceUsed" + q + """
                        ) },
                        new[]
                        {
                            new PackConfigField(
            """ + q + "telegram_token" + q + ", " + q + "Token" + q + """
                            , PackConfigFieldKind.Text),
                            new PackConfigField(
            """ + q + "llm_provider" + q + ", " + q + "Provider" + q + """
                            , PackConfigFieldKind.Choice, Choices: new[] {
            """ + q + "openai" + q + ", " + q + "ollama" + q + """
                            }),
                            new PackConfigField(
            """ + q + "llm_key" + q + ", " + q + "API Key" + q + """
                            , PackConfigFieldKind.Secret)
                        });
            }
            """;
    }

    [Given(@"a generic pack ""(.*)"" declaring 3 required config fields")]
    public void GivenAGenericConfiguredPack(string packName)
    {
        _packName = packName;
    }

    [When(@"I publish and install the pack")]
    public async Task WhenIPublishAndInstallThePack()
    {
        var market = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-config-form");
        await market.FireAsync(new PublishToMarketplace(
            _packName, "1.0", Code: ConfiguredPackSource(_packName), OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(_packName, "1.0", BuyerId: Scope));
    }

    [Then(@"a config form surface is emitted whose tree contains the fields ""(.*)"", ""(.*)"", ""(.*)""")]
    public async Task ThenAConfigFormSurfaceIsEmitted(string field1, string field2, string field3)
    {
        var gen = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + _packName.ToLowerInvariant());

        UiSurface? form = null;
        for (var attempt = 0; attempt < 40 && form is null; attempt++)
        {
            var timeline = await gen.GetTimelineAsync();
            form = timeline.OfType<UiSurface>().FirstOrDefault(s => s.Kind == ConfigFormSurface.Kind);
            if (form is null) await Task.Delay(50);
        }

        Assert.NotNull(form);
        Assert.Equal(_packName, form!.Props.GetValueOrDefault("pack"));

        var tree = Assert.IsType<UiWidgetTree>(form.Props["tree"]);
        var keys = CollectFieldKeys(tree);
        Assert.Contains(field1, keys);
        Assert.Contains(field2, keys);
        Assert.Contains(field3, keys);

        // The submit button round-trips a ConfigurationProvided carrying the pack name.
        var submit = FindNodes(tree).First(n => n.Type == DigitalBrain.Core.Ui.Button);
        Assert.Equal(nameof(ConfigurationProvided), submit.Props.GetValueOrDefault("synapseType"));
        Assert.Equal(_packName, submit.Props.GetValueOrDefault("pack"));
    }

    [When(@"I submit configuration for the pack with token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
    public async Task WhenISubmitConfiguration(string token, string provider, string key)
    {
        var values = new Dictionary<string, string>
        {
            ["telegram_token"] = token,
            ["llm_provider"] = provider,
            ["llm_key"] = key,
            ["pack"] = _packName,
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
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());
    }

    [Then(@"the pack config store returns token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
    public async Task ThenTheStoreReturnsValues(string token, string provider, string key)
    {
        var stored = await SharedConfigStore.GetAsync(Scope, _packName);
        Assert.Equal(token, stored["telegram_token"]);
        Assert.Equal(provider, stored["llm_provider"]);
        Assert.Equal(key, stored["llm_key"]);
    }

    private static IReadOnlyList<string> CollectFieldKeys(UiWidgetTree tree) =>
        FindNodes(tree)
            .Where(n => n.Type is var t && (t == DigitalBrain.Core.Ui.TextField || t == DigitalBrain.Core.Ui.Select))
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

    private sealed class ConfigFormSiloConfig : ISiloConfigurator
    {
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
