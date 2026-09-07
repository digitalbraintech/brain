using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace DigitalBrain.Core;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainRuntime
{
    public static void Add(ISiloBuilder builder, ModuleManifest modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddJournalStorage();
        builder.AddActivityPropagation();
        builder.UseJsonJournalFormat(DurableStateJson.TypeInfoResolver);
        ModelPayloadSerialization.AddModelPayloadSerialization(builder.Services);
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddSingleton<NeuronRuntime>();

        foreach (var hook in ModuleHooksOf(modules))
        {
            hook.Configure(builder);
        }
    }

    private static IEnumerable<IModule> ModuleHooksOf(ModuleManifest modules)
        => modules.Types.Select(static type =>
        {
            if (type is not { IsClass: true, IsAbstract: false }
                || !typeof(IModule).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Configured module '{type.FullName}' must be a concrete {nameof(IModule)} with a public parameterless constructor.");
            }

            return (IModule)Activator.CreateInstance(type)!;
        });
}
