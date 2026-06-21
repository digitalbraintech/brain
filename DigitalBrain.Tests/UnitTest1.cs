using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests;

public class NeuronTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task Neuron_Activates_And_Journals_NeuronActivated()
    {
        var grain = _cluster!.GrainFactory.GetGrain<IDemoNeuron>("demo1");
        var timeline = await grain.GetTimelineAsync();

        Assert.NotEmpty(timeline);
        Assert.Contains(timeline, s => s.Type == nameof(NeuronActivated));
    }

    [Fact]
    public async Task FireAsync_Persists_And_Replayable()
    {
        var grain = _cluster!.GrainFactory.GetGrain<IDemoNeuron>("demo2");
        await grain.FireAsync(new DemoMessageSynapse("hello from test"));

        var timeline = await grain.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(DemoMessageSynapse));
    }

    [Fact]
    public async Task SystemStatus_Launches_And_Records_Status()
    {
        var status = _cluster!.GrainFactory.GetGrain<ISystemStatus>("status-test");
        var timeline = await status.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(SystemLaunched) || s.Type == nameof(SystemStatusChanged));
    }

    private class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("Default")
                .ConfigureServices(services =>
                {
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("journal", (_, _) => new DigitalBrain.Silo.InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
                    services.AddSingleton<Orleans.Journaling.IJournaledStateManager, DigitalBrain.Silo.TestJournaledStateManager>();
                });
        }
    }
}
