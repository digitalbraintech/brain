using DigitalBrain.Core;
using DigitalBrain.Silo;
using DigitalBrain.Silo.Company;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Silo.Llm;
using Microsoft.Extensions.AI;
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
                services.AddScoped<NeuronJournals>();
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
                services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                services.AddSingleton<DocumentIngestor>();
                services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
                services.AddSingleton<SkillPackSynthesizer>();
                services.AddSingleton<HomeFeedBus>();
            });
    }
}

