namespace DigitalBrain.Protocol;

// The contract a typed-C# marketplace pack implements to embody behavior in the already-running silo.
// Pure and synchronous: the capability is Roslyn-compiled into a collectible AssemblyLoadContext under the
// CapabilityGate and dispatched to by the host GeneratedNeuron. This is the typed-C# replacement for the old
// LLM "personality" stub — the pack IS C#, never .ino. A pack assembly references only this Protocol assembly.
public interface IPackBehavior
{
    // Handle an input and return the pack's response. No I/O — the pack runs sandboxed by the CapabilityGate.
    string Respond(string input);
}

// Fired by the host when an embodied pack's REAL compiled code produces output. Its presence on the timeline
// is the proof that install -> compile -> ALC load -> dispatch actually ran the pack (vs. the LLM fallback).
[GenerateSerializer]
public record PackEmission(string Pack, string Input, string Output)
    : Synapse(nameof(PackEmission), DateTimeOffset.UtcNow);
