using DigitalBrain.Core;

namespace DigitalBrain.Silo;

// NET-NEW: Windows Package Manager neuron. No donor tree had one — built on the shared ProcessRunner.
[GrainType("digitalbrain.sdk.winget.v1")]
public class WingetNeuron : Neuron, IWingetNeuron
{
    public WingetNeuron(ILogger<WingetNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public Task<CommandResult> ListAsync(CancellationToken ct = default)
        => ProcessRunner.RunAsync("winget", "list --disable-interactivity", ct: ct);

    public Task<CommandResult> SearchAsync(string query, CancellationToken ct = default)
        => ProcessRunner.RunAsync("winget", $"search \"{query}\" --disable-interactivity --accept-source-agreements", ct: ct);

    public Task<CommandResult> UpgradeAllAsync(CancellationToken ct = default)
        => ProcessRunner.RunAsync("winget", "upgrade --all --disable-interactivity --accept-source-agreements --accept-package-agreements", timeoutMs: 600_000, ct: ct);

    public Task<CommandResult> InstallAsync(string packageId, CancellationToken ct = default)
        => ProcessRunner.RunAsync("winget", $"install --id {packageId} --disable-interactivity --accept-source-agreements --accept-package-agreements", timeoutMs: 600_000, ct: ct);
}

