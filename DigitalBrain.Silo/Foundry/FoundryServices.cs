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
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton<ISandboxedExecutor, OutOfProcessSandbox>();
            services.AddSingleton<IBuildRunner, ProcessBuildRunner>();
            var env = Environment.GetEnvironmentVariable("DIGITALBRAIN_ENV");
            if (string.Equals(env, "cloud", StringComparison.OrdinalIgnoreCase))
                services.AddSingleton<IResourceController, AzureResourceController>();
            else
                services.AddSingleton<IResourceController, AspireResourceController>();
        });
        return siloBuilder;
    }
}
