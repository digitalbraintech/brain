using DigitalBrain.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Silo;

public static class EconomicsServices
{
    // Registers the payment gateway: real Stripe when Stripe:SecretKey is configured, otherwise the synthetic
    // dev gateway with a loud warning so the absence of real payments is visible (no silent dev fallback).
    public static IServiceCollection AddEconomics(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]))
        {
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway>(sp =>
            {
                sp.GetService<ILoggerFactory>()?.CreateLogger("Economics").LogWarning(
                    "No Stripe:SecretKey configured — using the synthetic payment gateway (no real charges).");
                return new SyntheticPaymentGateway();
            });
        }
        return services;
    }
}
