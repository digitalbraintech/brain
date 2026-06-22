using DigitalBrain.Protocol;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Reqnroll;

namespace DigitalBrain.Tests.Steps;

[Binding]
public class CodeFoundrySteps : IAsyncDisposable
{
    private readonly TestCluster _cluster;
    private INeuron? _currentGrain;
    private IReadOnlyList<Synapse>? _timeline;

    public CodeFoundrySteps()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<FoundrySiloConfig>();
        _cluster = builder.Build();
        _cluster.DeployAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync() => await _cluster.StopAllSilosAsync();

    [Given(@"a code gen neuron ""(.*)""")]
    public async Task GivenACodeGenNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ICodeGenNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [When(@"I request generation of ""(.*)"" for tier ""(.*)""")]
    public async Task WhenIRequestGeneration(string spec, string tier)
    {
        var parsed = Enum.Parse<TargetTier>(tier);
        await _currentGrain!.FireAsync(new GenerateCode(spec, parsed));
        _timeline = await _currentGrain.GetTimelineAsync();
    }

    [Then(@"the timeline contains a CodeGenerated")]
    public async Task ThenTimelineContainsCodeGenerated()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(CodeGenerated));
    }

    private class FoundrySiloConfig : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("Default")
                .ConfigureServices(services =>
                {
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                    services.AddSingleton<Orleans.Journaling.IJournaledStateManager, TestJournaledStateManager>();
                    services.AddSingleton<ICodeExecutor, InProcessAlcExecutor>();
                    // uncomment after Task 6:
                    // services.AddSingleton<IBuildRunner, DigitalBrain.Tests.Foundry.FakeBuildRunner>();
                    // services.AddSingleton<IResourceController, DigitalBrain.Tests.Foundry.FakeResourceController>();
                });
        }
    }
}
