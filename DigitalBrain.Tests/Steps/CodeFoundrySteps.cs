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
    private readonly DigitalBrain.Tests.Foundry.FakeBuildRunner _sharedBuildRunner = new();
    private INeuron? _currentGrain;
    private IReadOnlyList<Synapse>? _timeline;

    public CodeFoundrySteps()
    {
        FoundrySiloConfig.BuildRunner = _sharedBuildRunner;
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<FoundrySiloConfig>();
        _cluster = builder.Build();
        _cluster.DeployAsync().GetAwaiter().GetResult();
        FoundrySiloConfig.BuildRunner = null;
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

    [Given(@"a code deploy neuron ""(.*)"" with verify-build (succeeding|failing)")]
    public async Task GivenACodeDeployNeuron(string id, string mode)
    {
        _sharedBuildRunner.NextResult = mode == "succeeding";
        _currentGrain = _cluster.GrainFactory.GetGrain<ICodeDeployNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [When(@"I deploy module ""(.*)"" with source ""(.*)""")]
    public async Task WhenIDeployModule(string module, string source)
    {
        await _currentGrain!.FireAsync(new DeployGeneratedCode(source, module));
        _timeline = await _currentGrain.GetTimelineAsync();
    }

    [Then(@"the timeline contains a CodeBuilt")]
    public async Task ThenTimelineContainsCodeBuilt()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(CodeBuilt));
    }

    [Then(@"the timeline contains a SiloRestartRequested")]
    public async Task ThenTimelineContainsSiloRestartRequested()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(SiloRestartRequested));
    }

    [Then(@"the timeline does not contain a SiloRestartRequested")]
    public async Task ThenTimelineDoesNotContainSiloRestartRequested()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.DoesNotContain(_timeline, s => s.Type == nameof(SiloRestartRequested));
    }

    [Then(@"the timeline contains a FoundryRolledBack")]
    public async Task ThenTimelineContainsFoundryRolledBack()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(FoundryRolledBack));
    }

    [Given(@"a foundry loop neuron ""(.*)""")]
    public async Task GivenAFoundryLoopNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ICodeFoundryLoopNeuron>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [When(@"I submit a foundry request ""(.*)"" for tier ""(.*)""")]
    public async Task WhenISubmitFoundryRequest(string spec, string tier)
    {
        var parsed = Enum.Parse<TargetTier>(tier);
        await _currentGrain!.FireAsync(new FoundryRequest(spec, parsed));
        _timeline = await _currentGrain.GetTimelineAsync();
    }

    [Then(@"the timeline contains a FoundryCheckpointed")]
    public async Task ThenTimelineContainsFoundryCheckpointed()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(FoundryCheckpointed));
    }

    [Then(@"the timeline contains a FoundryCompleted")]
    public async Task ThenTimelineContainsFoundryCompleted()
    {
        _timeline = await _currentGrain!.GetTimelineAsync();
        Assert.Contains(_timeline, s => s.Type == nameof(FoundryCompleted));
    }

    private class FoundrySiloConfig : ISiloConfigurator
    {
        // Populated by the CodeFoundrySteps constructor before cluster deploy so the silo
        // and the test step share the exact same FakeBuildRunner instance.
        internal static DigitalBrain.Tests.Foundry.FakeBuildRunner? BuildRunner;

        public void Configure(ISiloBuilder siloBuilder)
        {
            var runner = BuildRunner ?? new DigitalBrain.Tests.Foundry.FakeBuildRunner();
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("Default")
                .ConfigureServices(services =>
                {
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                    services.AddSingleton<Orleans.Journaling.IJournaledStateManager, TestJournaledStateManager>();
                    services.AddSingleton<ICodeExecutor, InProcessAlcExecutor>();
                    services.AddSingleton<IBuildRunner>(runner);
                    services.AddSingleton<IResourceController>(new DigitalBrain.Tests.Foundry.FakeResourceController());
                });
        }
    }
}
