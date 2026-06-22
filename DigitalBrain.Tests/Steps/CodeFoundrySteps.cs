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

    [Given(@"a code run neuron ""(.*)""")]
    public async Task GivenACodeRunNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ICodeRunNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [When(@"I run generated source returning text ""(.*)""")]
    public async Task WhenIRunGeneratedSource(string text)
    {
        var source = "public static class Module { public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input) => \"" + text + "\"; }";
        await _currentGrain!.FireAsync(new RunGeneratedCode(source));
        _timeline = await _currentGrain.GetTimelineAsync();
    }

    [When(@"I run invalid generated source")]
    public async Task WhenIRunInvalidSource()
    {
        await _currentGrain!.FireAsync(new RunGeneratedCode("public class Broken { not valid }"));
        _timeline = await _currentGrain.GetTimelineAsync();
    }

    [Then(@"the timeline contains a CodeRunResult")]
    public async Task ThenTimelineContainsCodeRunResult()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(CodeRunResult));
    }

    [Then(@"the last CodeRunResult is successful with output containing ""(.*)""")]
    public async Task ThenLastRunResultSuccessful(string fragment)
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        var result = _timeline.OfType<CodeRunResult>().Last();
        Assert.True(result.Success, result.Error);
        Assert.Contains(fragment, result.Output);
    }

    [Then(@"the last CodeRunResult is a failure")]
    public async Task ThenLastRunResultFailure()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        var result = _timeline.OfType<CodeRunResult>().Last();
        Assert.False(result.Success);
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
