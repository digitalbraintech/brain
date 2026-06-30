using System.Collections.Generic;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Company;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Ui;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.TestSupport;

// No-op scoped factory for shared test clusters: always defers to the global IChatClient by returning null.
// Prevents shared-config tests from acquiring a hidden Ollama/OpenAI network dependency.
// Tests that need the recording factory override this via their own ISiloConfigurator.
internal sealed class NoOpScopedChatClientFactory : IScopedChatClientFactory
{
    public IChatClient? Create(string provider, string? apiKey) => null;
}

// Shared TestCluster silo wiring: in-memory dual journals + the pack embodiment engine.
// Reused by every TestCluster-based test so the prototype journal + Foundry services are configured once.
public sealed class NeuronTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
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
                services.AddSingleton<IScopedChatClientFactory, NoOpScopedChatClientFactory>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                services.AddSingleton<DocumentIngestor>();
                services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
                services.AddSingleton<SkillPackSynthesizer>();
                services.AddSingleton<HomeFeedBus>();
                services.AddHomeFeedStreamSubscriber();
                services.AddSingleton<SignalEgressBus>();
                services.AddSignalEgressStreamSubscriber();
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

