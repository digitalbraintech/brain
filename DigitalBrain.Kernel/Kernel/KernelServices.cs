using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel;

public static class KernelServices
{
    // Registers checkpoint encryption. The key comes from ICheckpointKeyProvider (config today, Key Vault later).
    // AES-GCM when a key is present; in Production a missing key fails fast; in dev it falls back to PassThrough
    // with a loud warning so the absence of encryption is never silent.
    public static IServiceCollection AddKernelSecurity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var keyProvider = new ConfigCheckpointKeyProvider(configuration);
        services.AddSingleton<ICheckpointKeyProvider>(keyProvider);
        var key = keyProvider.GetKey();

        if (key is not null)
        {
            services.AddSingleton<INeuronStateProtector>(new AesNeuronStateProtector(key));
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "DigitalBrain:Checkpoint:Key is required in Production (checkpoints must be encrypted). " +
                "Supply it from Key Vault via an ICheckpointKeyProvider.");
        }
        else
        {
            services.AddSingleton<INeuronStateProtector>(sp =>
            {
                sp.GetService<ILoggerFactory>()?.CreateLogger("KernelSecurity").LogWarning(
                    "No DigitalBrain:Checkpoint:Key configured — checkpoints are NOT encrypted (PassThrough). " +
                    "Configure a key (Key Vault) before production.");
                return new PassThroughNeuronStateProtector();
            });
        }

        services.AddSingleton<CheckpointProtector>();
        return services;
    }
}
