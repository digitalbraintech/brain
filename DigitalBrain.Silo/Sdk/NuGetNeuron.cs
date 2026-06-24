using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// Typed NuGet neuron via the dotnet CLI; supersedes the untyped NuGetManagerNeuron.
[GrainType("digitalbrain.sdk.nuget.v1")]
public class NuGetNeuron : Neuron, INuGetNeuron
{
    public NuGetNeuron(ILogger<NuGetNeuron> logger) : base(logger) { }

    public Task<CommandResult> ListPackagesAsync(string project, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", $"list \"{project}\" package", ct: ct);

    public Task<CommandResult> ListOutdatedAsync(string project, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", $"list \"{project}\" package --outdated", ct: ct);

    public Task<CommandResult> RestoreAsync(string project, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", $"restore \"{project}\"", ct: ct);

    public Task<CommandResult> AddPackageAsync(string project, string package, string? version = null, CancellationToken ct = default)
        => ProcessRunner.RunAsync(
            "dotnet",
            version is null
                ? $"add \"{project}\" package {package}"
                : $"add \"{project}\" package {package} --version {version}",
            ct: ct);
}
