using DigitalBrain.Protocol;
using DigitalBrain.Silo.Foundry;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.TestSupport;

// Shared TestCluster silo wiring: in-memory dual journals + the pack embodiment engine.
// Reused by every TestCluster-based test so the prototype journal + Foundry services are configured once.
public sealed class NeuronTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryStreams("Default")
            .ConfigureServices(services =>
            {
                services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
                services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            });
    }
}
