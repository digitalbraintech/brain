using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Product.Presentation;

namespace DigitalBrain.Kernel;

internal sealed record BrainGraphNeuronMetadata(string Label, string Module, string? IconKey, IReadOnlyList<string> HandledSignals);

internal sealed class BrainGraphMetadata(IEnumerable<NeuronPresentation> presentations)
{
    private readonly IReadOnlyDictionary<string, NeuronPresentation> _presentations =
        presentations.ToDictionary(item => item.NeuronType, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BrainGraphNeuronMetadata> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Resolve metadata only for already-observed grain types. This is neither an
    // instance catalog nor permission to create a relationship from a capability.
    internal BrainGraphNeuronMetadata For(string grainType) => _cache.GetOrAdd(grainType, Resolve);

    private BrainGraphNeuronMetadata Resolve(string grainType)
    {
        var inferred = Infer(grainType);
        return _presentations.TryGetValue(grainType, out var presentation)
            ? inferred with { Label = presentation.Label, Module = presentation.Module, IconKey = presentation.IconKey }
            : inferred;
    }

    private static BrainGraphNeuronMetadata Infer(string grainType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.GetName().Name?.StartsWith("DigitalBrain", StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(Neuron).IsAssignableFrom(type))
                {
                    continue;
                }

                var declared = type.GetCustomAttributesData().FirstOrDefault(attribute =>
                    attribute.AttributeType == typeof(GrainTypeAttribute));
                var declaredType = declared?.ConstructorArguments.FirstOrDefault().Value as string ?? type.Name;
                if (!string.Equals(declaredType, grainType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var assemblyName = assembly.GetName().Name!;
                const string modulePrefix = "DigitalBrain.Modules.";
                var module = assemblyName.StartsWith(modulePrefix, StringComparison.Ordinal)
                    ? assemblyName[modulePrefix.Length..].Split('.')[0] : "Kernel";
                var label = grainType switch
                {
                    "chat" => "Results",
                    "activities" => "Activities",
                    "activitysource" => "Execution events",
                    "uirenderer" => "UI renderer",
                    "chat-turn-worker" => "Turn worker",
                    "assistant" => "Ino",
                    "sessionneuron" => "Owner session",
                    _ => type.Name.EndsWith("Neuron", StringComparison.Ordinal) ? type.Name[..^6] : type.Name,
                };
                return new(label, module, BuiltInIcon(grainType), [.. type.GetInterfaces()
                    .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IHandle<>))
                    .Select(contract => contract.GetGenericArguments()[0].Name)
                    .Where(IsSubscriptionSignal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
            }
        }

        return new(grainType, "Kernel", BuiltInIcon(grainType), []);
    }

    private static string? BuiltInIcon(string grainType) => grainType switch
    {
        "assistant" => "assistant",
        "chat" => "conversation",
        "chat-turn-worker" or "execution" => "execution",
        "sessionneuron" or "memory" => "memory",
        "behaviors" => "repository",
        "timer" => "clock",
        _ => null,
    };

    // AgentActivity is recorded evidence; it is not a published lifecycle contract.
    internal static bool IsSubscriptionSignal(string signalType)
        => !string.IsNullOrWhiteSpace(signalType)
            && signalType is not (nameof(Subscribe) or nameof(Unsubscribe) or nameof(AgentActivity));
}
