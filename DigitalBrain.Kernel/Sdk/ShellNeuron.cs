using DigitalBrain.Core;
using DigitalBrain.Windows;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.sdk.shell.v1")]
public class ShellNeuron(ILogger<ShellNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IShellNeuron
{
    public Task<CommandResult> ExecuteAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
        => ProcessRunner.ShellAsync(command, workingDirectory, timeoutMs, ct);

    public Task<CommandResult> RunDotnetAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", arguments, workingDirectory, ct: ct);

    public Task<CommandResult> ExecutePowerShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
        => ProcessRunner.PowerShellAsync(command, workingDirectory, timeoutMs, ct);
}

