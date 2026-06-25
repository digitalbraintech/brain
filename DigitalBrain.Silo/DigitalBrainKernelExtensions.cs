using DigitalBrain.Silo.Company;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Silo.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace DigitalBrain.Silo;

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
            siloBuilder.AddFoundry();

            // Built-in neurons discovered automatically.
        });

        builder.Services.AddDigitalBrainChat(builder.Configuration);
        builder.Services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
        builder.Services.AddSingleton<SkillPackSynthesizer>();

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient();
        return builder;
    }
}
