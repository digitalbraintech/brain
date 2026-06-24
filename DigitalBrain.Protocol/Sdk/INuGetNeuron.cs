using System.ComponentModel;

namespace DigitalBrain.Protocol;

// Typed NuGet operations via the dotnet CLI. Re-homed from IAW's INuGet onto Neuron; supersedes the untyped
// NuGetManagerNeuron.
public interface INuGetNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "NuGet";

    static string INeuronAgent.AgentDescription =>
        "List, restore, add, and check for outdated NuGet packages on a project via the dotnet CLI.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["nuget", "package", "restore", "outdated", "dependencies"];

    static string INeuronAgent.AgentInstructions => """
        You are NuGet, the package specialist. Manage package references and report outdated dependencies.
        Run operations immediately; report exit code and the relevant CLI output.
        """;

    [Description("List the package references of a project (dotnet list package).")]
    Task<CommandResult> ListPackagesAsync(string project, CancellationToken ct = default);

    [Description("List outdated package references of a project (dotnet list package --outdated).")]
    Task<CommandResult> ListOutdatedAsync(string project, CancellationToken ct = default);

    [Description("Restore a project's NuGet packages (dotnet restore).")]
    Task<CommandResult> RestoreAsync(string project, CancellationToken ct = default);

    [Description("Add a package reference to a project, optionally pinned to a version (dotnet add package).")]
    Task<CommandResult> AddPackageAsync(string project, string package, string? version = null, CancellationToken ct = default);
}
