using DigitalBrain.Context;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Context;

public class ContextRecallTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task Remembers_And_Recalls_By_Relevance()
    {
        var context = _cluster!.GrainFactory.GetGrain<IContextNeuron>("ctx-recall");
        await context.RememberAsync("set an alarm clock for 7am");
        await context.RememberAsync("buy milk and eggs from the store");
        await context.RememberAsync("git commit and push the feature branch");

        // With the NoOp embedder, recall degrades to keyword scoring via the hybrid scorer.
        var hits = await context.RecallAsync("alarm", top: 2);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Contains("alarm", System.StringComparison.OrdinalIgnoreCase));
    }
}

