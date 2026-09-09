using Microsoft.Extensions.Configuration;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public sealed record ModuleManifest(IReadOnlyList<Type> Types)
{
    public static ModuleManifest FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var types = configuration
            .GetSection(DigitalBrainNames.Modules)
            .GetChildren()
            .OrderBy(static entry => int.TryParse(entry.Key, out var index) ? index : int.MaxValue)
            .Select(static entry => Resolve(entry.Value))
            .ToArray();

        if (types.Length == 0)
        {
            throw new InvalidOperationException(
                $"No modules are configured at '{DigitalBrainNames.Modules}'. AppHost must declare each module with AddModule<TModule>().");
        }

        return new ModuleManifest(types);
    }

    private static Type Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"'{DigitalBrainNames.Modules}' contains an empty module type.");
        }

        return Type.GetType(name, throwOnError: false)
            ?? throw new InvalidOperationException($"Configured module type '{name}' could not be loaded.");
    }
}
