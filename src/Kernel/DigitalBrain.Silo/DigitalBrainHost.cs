using DigitalBrain.Aspire;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Kernel;

internal static class DigitalBrainHost
{
    internal static IHostApplicationBuilder AddDigitalBrain(this IHostApplicationBuilder builder)
        => builder.AddDigitalBrain(ModuleManifest.FromConfiguration(builder.Configuration));
}
