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

            // Centralized prototype journals (single source in PrototypeJournals).
            siloBuilder.ConfigurePrototypeJournals();

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
