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
            // Auto population via incoming grain call filter + explicit in Neuron Fire/Deliver.
            siloBuilder.ConfigureServices(services =>
            {
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddSingleton<Orleans.Journaling.IJournaledStateManager, PrototypeJournaledStateManager>();
            });

            // Call filter ensures every incoming synapse (DeliverAsync or grain invocation) auto-logs to receiver's in-journal.
            siloBuilder.AddIncomingGrainCallFilter<IncomingJournalFilter>();

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



// Incoming call filter for dual-journal auto population.
internal sealed class IncomingJournalFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        await context.Invoke();
    }
}