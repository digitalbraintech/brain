namespace DigitalBrain.Protocol;

// Foundation for typed integration neurons (the SDK layer): file system, shell, git, dotnet, nuget, roslyn, …
// Each contract is an Orleans grain reached by typed RPC (zero-reflection dispatch) and carries
// compiler-verified metadata via C# static virtual interface members, read generically through
// NeuronAgentMetadata.ReadFrom<TContract>() with no reflection at the call site.
// Pattern harvested from IAW (E:\DigitalBrainTech\IAW Core/Contracts/IAgent.cs), slimmed to drop the
// LLM-chat/scheduling surface — infra neurons are request/response, not conversational.
public interface INeuronAgent : INeuron
{
    static virtual string AgentDisplayName => "";
    static virtual string AgentDescription => "";
    static virtual string[] AgentCapabilities => [];
    static virtual string AgentInstructions => "You are a typed integration neuron.";
    static virtual string[] AgentRoutingExamples => [];
}

public readonly record struct NeuronAgentMetadata(
    string DisplayName,
    string Description,
    string[] Capabilities,
    string Instructions,
    string[] RoutingExamples)
{
    // Zero-reflection: the compiler resolves the static abstract members at this call site.
    public static NeuronAgentMetadata ReadFrom<TContract>() where TContract : INeuronAgent =>
        new(
            TContract.AgentDisplayName,
            TContract.AgentDescription,
            TContract.AgentCapabilities,
            TContract.AgentInstructions,
            TContract.AgentRoutingExamples);
}
