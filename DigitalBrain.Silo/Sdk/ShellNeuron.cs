using DigitalBrain.Core;

namespace DigitalBrain.Silo;

[GrainType("digitalbrain.sdk.shell.v1")]
public class ShellNeuron : Neuron, IShellNeuron
{
    public ShellNeuron(ILogger<ShellNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public Task<CommandResult> ExecuteAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
        => ProcessRunner.ShellAsync(command, workingDirectory, timeoutMs, ct);

    public Task<CommandResult> RunDotnetAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default)
        => ProcessRunner.RunAsync("dotnet", arguments, workingDirectory, ct: ct);

    public Task<CommandResult> ExecutePowerShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
        => ProcessRunner.PowerShellAsync(command, workingDirectory, timeoutMs, ct);
}

