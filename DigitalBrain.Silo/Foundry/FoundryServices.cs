using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Silo.Foundry;

public static class FoundryServices
{
    public static ISiloBuilder AddFoundry(this ISiloBuilder siloBuilder)
    {
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<ICodeExecutor, InProcessAlcExecutor>();
            services.AddSingleton<IBuildRunner, ProcessBuildRunner>();
            services.AddSingleton<IResourceController, AspireResourceController>();
        });
        return siloBuilder;
    }
}
