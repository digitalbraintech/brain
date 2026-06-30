namespace DigitalBrain.Core;

public enum PackConfigFieldKind { Text, Secret, Choice }

[GenerateSerializer]
public record PackConfigField(
    [property: Id(0)] string Key,
    [property: Id(1)] string Label,
    [property: Id(2)] PackConfigFieldKind Kind,
    [property: Id(3)] IReadOnlyList<string>? Choices = null,
    [property: Id(4)] string? DependsOnKey = null,
    [property: Id(5)] string? DependsOnValue = null);

// The contract a typed-C# marketplace pack implements to embody behavior in the already-running silo.
// Pure and synchronous: the capability is Roslyn-compiled into a collectible AssemblyLoadContext under the
// CapabilityGate and dispatched to by the host GeneratedNeuron. This is the typed-C# replacement for the old
// LLM "personality" stub — the pack IS C#, never .ino. A pack assembly references only this Protocol assembly.
[GenerateSerializer]
public record PackManifest(
    [property: Id(0)] IReadOnlyList<SynapseType> HandledSynapseTypes,
    [property: Id(1)] IReadOnlyList<PackConfigField>? RequiredConfig = null);

public interface IPackBehavior
{
    string Respond(string input);

    PackManifest GetManifest() => new(new[] { new SynapseType(nameof(ExperienceUsed)) });

    bool CanHandle(Synapse synapse) =>
        GetManifest().HandledSynapseTypes.Any(t => t.Value == synapse.Type) || synapse is ExperienceUsed;

    IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is ExperienceUsed used)
        {
            return [new PackEmission(string.Empty, used.Action, Respond(used.Action))];
        }

        return Array.Empty<Synapse>();
    }
}

// Fired by the host when an embodied pack's REAL compiled code produces output. Its presence on the timeline
// is the proof that install -> compile -> ALC load -> dispatch actually ran the pack (vs. the LLM fallback).
[GenerateSerializer]
public record PackEmission(string Pack, string Input, string Output)
    : Synapse(nameof(PackEmission), DateTimeOffset.UtcNow);
