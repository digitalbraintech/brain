using System.ComponentModel;

namespace DigitalBrain.Protocol;

// Typed Roslyn code-intelligence. Wraps the real MSBuildWorkspace solution analysis that previously lived in the
// untyped RoslynArchitectNeuron (reuse-first), behind a typed contract with static-virtual metadata.
public interface IRoslynNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "Roslyn";

    static string INeuronAgent.AgentDescription =>
        "Analyze .NET solutions with Roslyn (MSBuildWorkspace): project inventory and compiler diagnostics.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["roslyn", "analyze", "diagnostics", "compilation", "code-intelligence"];

    static string INeuronAgent.AgentInstructions => """
        You are Roslyn, the code-intelligence specialist. Open solutions and report structure and diagnostics.
        Do NOT build (use DotNet) or edit files (use FileSystem).
        """;

    [Description("Open a solution with MSBuildWorkspace and report its project count and sample compiler errors.")]
    Task<string> AnalyzeSolutionAsync(string solutionPath, CancellationToken ct = default);
}
