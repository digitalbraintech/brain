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
        // Core Orleans host setup - localhost for fast "dotnet run" experience
        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();

            // Journaling for synapse timelines (prototype in-memory for fast boot)
            siloBuilder.ConfigureServices(services =>
            {
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddSingleton<Orleans.Journaling.IJournaledStateManager, PrototypeJournaledStateManager>();
            });

            // Built-in neurons are discovered automatically because the calling assembly references DigitalBrain.Silo.
            // For explicit control in advanced scenarios, use siloBuilder.ConfigureApplicationParts(...) 

        });

        // Ollama for LLM-powered neurons (Compiler, LlmNeuron, etc.)
        // Assumes Ollama is available (or will fallback in neurons)
        builder.AddOllamaApiClient("qwen");

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient();
        return builder;
    }
}

// The in-memory journal helpers (kept minimal for the fast path)
internal sealed class InMemoryJournalForPrototype<T> : List<T>, Orleans.Journaling.IDurableList<T>;
internal sealed class PrototypeJournaledStateManager : Orleans.Journaling.IJournaledStateManager
{
    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public void RegisterState(string stateId, Orleans.Journaling.IJournaledState state) { }
    public bool TryGetState(string stateId, out Orleans.Journaling.IJournaledState? state) { state = null; return false; }
    public ValueTask WriteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DeleteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}