using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Developer;

// Typed .NET CLI operations (build/test/version/list). Re-homed from IAW's IDotNet onto Neuron.
public interface IDotNetNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "DotNet";

    static string INeuronAgent.AgentDescription =>
        "Build, test, and inspect .NET projects and solutions via the dotnet CLI.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["dotnet", "build", "test", "msbuild", "project", "solution"];

    static string INeuronAgent.AgentInstructions => """
        You are DotNet, the .NET build specialist. Build and test projects/solutions and report diagnostics.
        Run operations immediately; report exit code and the relevant build/test output.
        """;

    [Description("Build a project or solution (dotnet build --nologo). Returns exit code and output.")]
    Task<CommandResult> BuildAsync(string projectOrSolution, CancellationToken ct = default);

    [Description("Run the tests of a project or solution (dotnet test --nologo). Returns exit code and output.")]
    Task<CommandResult> TestAsync(string projectOrSolution, CancellationToken ct = default);

    [Description("Return the installed .NET SDK version (dotnet --version).")]
    Task<string> VersionAsync(CancellationToken ct = default);

    [Description("List all .csproj files under a directory (recursive).")]
    Task<string[]> ListProjectsAsync(string directory, CancellationToken ct = default);
}
