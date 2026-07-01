using DigitalBrain.Core;
using DigitalBrain.Developer;
using DigitalBrain.Windows;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.sdk.dotnet.v1")]
public class DotNetNeuron(ILogger<DotNetNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IDotNetNeuron
{
    public Task<CommandResult> BuildAsync(string projectOrSolution, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", $"build \"{projectOrSolution}\" --nologo", ct: ct);

    public Task<CommandResult> TestAsync(string projectOrSolution, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", $"test \"{projectOrSolution}\" --nologo", ct: ct);

    public async Task<string> VersionAsync(CancellationToken ct = default)
        => (await ProcessRunner.RunAsync("dotnet", "--version", ct: ct)).Output.Trim();

    public Task<string[]> ListProjectsAsync(string directory, CancellationToken ct = default)
        => Task.FromResult(Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories));
}

