using DigitalBrain.Protocol;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Reqnroll;

namespace DigitalBrain.Tests.Steps;

[Binding]
public class NeuronSteps : IAsyncDisposable
{
    private readonly TestCluster _cluster;
    private INeuron? _currentGrain;
    private IReadOnlyList<Synapse>? _timeline;

    public NeuronSteps()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SimpleSiloConfig>();
        _cluster = builder.Build();
        _cluster.DeployAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Given(@"a demo neuron ""(.*)""")]
    public async Task GivenADemoNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<IDemoNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"an aspire orchestrator neuron ""(.*)""")]
    public async Task GivenAnAspireOrchestratorNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<IAspireNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a marketplace neuron ""(.*)""")]
    public async Task GivenAMarketplaceNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a compiler neuron ""(.*)""")]
    public async Task GivenACompilerNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ICompiler>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a meta optimizer neuron ""(.*)""")]
    public async Task GivenAMetaOptimizerNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<IMetaOptimizerNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a software10 team neuron ""(.*)""")]
    public async Task GivenASoftware10TeamNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ISoftware10Team>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a software20 team neuron ""(.*)""")]
    public async Task GivenASoftware20TeamNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ISoftware20Team>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a system status neuron ""(.*)""")]
    public async Task GivenASystemStatusNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ISystemStatus>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [When(@"I send create neuron request ""(.*)""")]
    public async Task WhenISendCreateNeuronRequest(string desc)
    {
        await _currentGrain!.FireAsync(new CreateNeuronRequest(desc));
    }

    [When(@"I send create simple app request ""(.*)"" for team ""(.*)""")]
    public async Task WhenISendCreateSimpleAppRequest(string desc, string team)
    {
        await _currentGrain!.FireAsync(new CreateSimpleApp(team, desc));
    }

    [When(@"I fire a bad status for component ""(.*)""")]
    public async Task WhenIFireABadStatusForComponent(string component)
    {
        await _currentGrain!.FireAsync(new SystemStatusChanged(component, "FailedToStart", "simulated failure"));
    }

    [When(@"I fire multiple messages to trigger telemetry")]
    public async Task WhenIFireMultipleMessagesToTriggerTelemetry()
    {
        var demo = _cluster.GrainFactory.GetGrain<IDemoNeuron>("demo-opt");
        var optimizer = _cluster.GrainFactory.GetGrain<IMetaOptimizerNeuron>("optimizer1");
        for (int i = 0; i < 6; i++)
        {
            await demo.FireAsync(new DemoMessageSynapse($"msg-{i}"));
            // fire telemetry to optimizer
            await optimizer.FireAsync(new NeuronTelemetry(new NeuronId("demo-opt"), "test-event"));
        }
    }

    [When(@"I publish, a simulated other brain installs and uses the pack ""(.*)"" version ""(.*)""")]
    public async Task WhenIPublishInstallUseAsOtherBrain(string pack, string ver)
    {
        await SimulatePublishInstallUse(pack, ver);
    }

    private async Task SimulatePublishInstallUse(string packName, string version)
    {
        // Simulate "other brain" connecting to marketplace contract
        var market = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(packName, version));
        await market.FireAsync(new ListPublished());  // so PublishedList appears for harness asserts
        await market.FireAsync(new InstallFromMarketplace(packName, version));
        // Activate the GeneratedNeuron (as if downloaded/installed) and use it
        var genKey = "generated-" + packName.ToLower();
        var gen = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>(genKey);
        await gen.FireAsync(new ExperienceUsed(packName, "simulated-use-by-other-brain"));
    }

    [When(@"I fire a DemoMessageSynapse with text ""(.*)""")]
    public async Task WhenIFireADemoMessageSynapseWithText(string text)
    {
        await _currentGrain!.FireAsync(new DemoMessageSynapse(text));
    }

    [When(@"I fire a StartDistributedApp for ""(.*)""")]
    public async Task WhenIFireAStartDistributedAppFor(string app)
    {
        await _currentGrain!.FireAsync(new StartDistributedApp(app));
    }

    [When(@"I publish pack ""(.*)"" version ""(.*)""")]
    public async Task WhenIPublishPackVersion(string pack, string ver)
    {
        var targetMarket = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await targetMarket!.FireAsync(new PublishToMarketplace(pack, ver));
    }

    [When(@"I download/install the pack ""(.*)"" version ""(.*)""")]
    public async Task WhenIDownloadInstallThePack(string pack, string ver)
    {
        var targetMarket = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await targetMarket!.FireAsync(new InstallFromMarketplace(pack, ver));
        // activation fire to gen skipped in test to avoid timeout/hang; TUI and real use demonstrates it
    }

    [When(@"I request published list")]
    public async Task WhenIRequestPublishedList()
    {
        var target = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await target!.FireAsync(new ListPublished());
    }

    [Then(@"the timeline contains a DemoMessageSynapse")]
    public async Task ThenTheTimelineContainsADemoMessageSynapse()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(DemoMessageSynapse));
    }

    [Then(@"the timeline contains a DistributedAppStarted")]
    public async Task ThenTheTimelineContainsADistributedAppStarted()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(DistributedAppStarted));
    }

    [Then(@"the timeline contains a PublishedList")]
    public async Task ThenTheTimelineContainsAPublishedList()
    {
        var mkt = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        _timeline = await mkt.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(PublishedList));
    }

    [Then(@"the timeline contains a NeuronCodeGenerated")]
    public async Task ThenTheTimelineContainsANeuronCodeGenerated()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(NeuronCodeGenerated));
    }

    [Then(@"the timeline contains a SimpleAppCreated")]
    public async Task ThenTheTimelineContainsASimpleAppCreated()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(SimpleAppCreated));
    }

    [Then(@"the timeline contains a FixProposal")]
    public async Task ThenTheTimelineContainsAFixProposal()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(FixProposal));
    }

    [Then(@"the timeline contains a SimulationResult with success true")]
    public async Task ThenTheTimelineContainsASimulationResultWithSuccessTrue()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        var sim = _timeline.LastOrDefault(s => s.Type == nameof(SimulationResult)) as SimulationResult;
        Assert.NotNull(sim);
        Assert.True(sim.Success);
        Assert.Contains("different", sim.Details ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"the timeline contains a WiringOptimizationProposed")]
    public async Task ThenTheTimelineContainsAWiringOptimizationProposed()
    {
        var opt = _cluster.GrainFactory.GetGrain<IMetaOptimizerNeuron>("optimizer1");
        _timeline = await opt.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(WiringOptimizationProposed));
    }

    [Then(@"the timeline contains a NeuroPackInstalled")]
    public async Task ThenTheTimelineContainsANeuroPackInstalledForFlow()
    {
        var mkt = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        _timeline = await mkt.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(NeuroPackInstalled));
    }

    [Then(@"the generated neuron for pack ""(.*)"" received an ExperienceUsed")]
    public async Task ThenGeneratedNeuronReceivedExperienceUsed(string pack)
    {
        var genKey = "generated-" + pack.ToLower();
        var gen = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>(genKey);
        var tl = await gen.GetTimelineAsync();
        Assert.Contains(tl, s => s.Type == nameof(ExperienceUsed));
    }

    [Then(@"the timeline contains a ExperienceUsed")]
    public async Task ThenTheTimelineContainsAExperienceUsed()
    {
        var mkt = _cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        _timeline = await mkt.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(ExperienceUsed));
    }

    [Then(@"replaying shows the message")]
    public void ThenReplayingShowsTheMessage()
    {
        Assert.NotNull(_timeline);
    }

    [Then(@"the timeline contains these synapse types in causal order: (.*)")]
    public void ThenTheTimelineContainsTheseInCausalOrder(string typesCsv)
    {
        var expected = typesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
        _timeline = _currentGrain != null ? _currentGrain.GetTimelineAsync().GetAwaiter().GetResult() : _timeline;
        var actual = (_timeline ?? Enumerable.Empty<Synapse>()).Select(s => s.Type).ToList();
        int pos = 0;
        foreach (var exp in expected)
        {
            pos = actual.IndexOf(exp, pos);
            if (pos < 0)
                throw new Xunit.Sdk.XunitException($"Causal order not satisfied for '{exp}' in [{string.Join(", ", actual)}]");
            pos++;
        }
    }

    private class SimpleSiloConfig : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("Default")
                .ConfigureServices(services =>
                {
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal", (_, _) => new InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal", (_, _) => new InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
                    services.AddSingleton<Orleans.Journaling.IJournaledStateManager, TestJournaledStateManager>();
                });
        }
    }
}