using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

internal static class Providers
{
    // A missing provider is a rejected signal, not a crash: the caller named something the
    // host never configured, and the message says exactly where to configure it.
    internal static IChatClient Resolve(IServiceProvider services, string? provider)
    {
        ArgumentNullException.ThrowIfNull(services);

        var defaults = services.GetRequiredService<AIDefaults>();
        var name = string.IsNullOrWhiteSpace(provider) ? defaults.DefaultProvider : provider;
        return services.GetKeyedService<IChatClient>(name)
            ?? throw new SignalRejectedException(
                $"Provider '{name}' is not configured. Configure DigitalBrain:AI:Providers:{name} "
                + $"or omit provider to use '{defaults.DefaultProvider}'.");
    }
}
