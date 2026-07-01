using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Developer;

// Typed Roslyn code-intelligence. Wraps the real MSBuildWorkspace solution analysis that previously lived in the
// untyped RoslynArchitectNeuron (reuse-first), behind a typed contract with static-virtual metadata.
public interface IRoslynNeuron : IAgent
{
    static string IAgent.AgentDisplayName => "Roslyn";

    static string IAgent.AgentDescription =>
        "Analyze .NET solutions with Roslyn (MSBuildWorkspace): project inventory and compiler diagnostics.";

    static string[] IAgent.AgentCapabilities =>
        ["roslyn", "analyze", "diagnostics", "compilation", "code-intelligence"];

    static string IAgent.AgentInstructions => """
        You are Roslyn, the code-intelligence specialist. Open solutions and report structure and diagnostics.
        Do NOT build (use DotNet) or edit files (use FileSystem).
        """;

    [Description("Open a solution with MSBuildWorkspace and report its project count and sample compiler errors.")]
    Task<string> AnalyzeSolutionAsync(string solutionPath, CancellationToken ct = default);
}
