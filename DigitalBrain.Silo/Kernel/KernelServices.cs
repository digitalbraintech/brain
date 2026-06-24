using DigitalBrain.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Silo;

public static class KernelServices
{
    // Registers checkpoint encryption. AES-GCM when a base64 key is configured (DigitalBrain:Checkpoint:Key,
    // sourced from Key Vault in cloud); otherwise a PassThrough protector with a loud warning for local dev.
    public static IServiceCollection AddKernelSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var keyBase64 = configuration["DigitalBrain:Checkpoint:Key"];
        if (!string.IsNullOrWhiteSpace(keyBase64))
        {
            var key = Convert.FromBase64String(keyBase64);
            services.AddSingleton<INeuronStateProtector>(new AesNeuronStateProtector(key));
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
