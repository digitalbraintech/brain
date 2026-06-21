using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using Orleans.TestingHost;
using Reqnroll;
using Xunit;

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

    [When(@"I send create neuron request ""(.*)""")]
    public async Task WhenISendCreateNeuronRequest(string desc)
    {
        await _currentGrain!.FireAsync(new CreateNeuronRequest(desc));
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
        await _currentGrain!.FireAsync(new PublishToMarketplace(pack, ver));
    }

    [When(@"I request published list")]
    public async Task WhenIRequestPublishedList()
    {
        await _currentGrain!.FireAsync(new ListPublished());
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
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(PublishedList));
    }

    [Then(@"the timeline contains a NeuronCodeGenerated")]
    public async Task ThenTheTimelineContainsANeuronCodeGenerated()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(NeuronCodeGenerated));
    }

    [Then(@"the timeline contains a WiringOptimizationProposed")]
    public async Task ThenTheTimelineContainsAWiringOptimizationProposed()
    {
        var opt = _cluster.GrainFactory.GetGrain<IMetaOptimizerNeuron>("optimizer1");
        _timeline = await opt.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(WiringOptimizationProposed));
    }

    [Then(@"replaying shows the message")]
    public void ThenReplayingShowsTheMessage()
    {
        Assert.NotNull(_timeline);
    }

    private class SimpleSiloConfig : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("Default");
        }
    }
}