using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace DigitalBrain.Silo;

/// <summary>
/// Encapsulates the DigitalBrain "Kernel" (runtime host for neurons/synapses).
/// This is the core integration point for client and host.
/// </summary>
public static class DigitalBrainKernelExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainKernel(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();

            // Dual journals: incoming (received Deliver) + outgoing (Fire). Prototype in-memory for fast single-process boot.
            // Population explicit in Neuron.FireAsync / DeliverAsync for synapse causality.
            siloBuilder.ConfigureServices(services =>
            {
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddSingleton<Orleans.Journaling.IJournaledStateManager, PrototypeJournaledStateManager>();
            });

            // Built-in neurons discovered automatically.
        });

        builder.AddOllamaApiClient("qwen");

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient();
        return builder;
    }
}
