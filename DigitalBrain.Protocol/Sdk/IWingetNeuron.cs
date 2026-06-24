using System.ComponentModel;

namespace DigitalBrain.Protocol;

// Typed Windows Package Manager (winget) operations. NET-NEW: no donor tree had a WingetNeuron — built on the
// shared ProcessRunner shell mechanics (modeled on IAW's ShellAgent). Windows-only; mutating ops run winget directly.
public interface IWingetNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "Winget";

    static string INeuronAgent.AgentDescription =>
        "Manage Windows applications via winget: list installed, search, install, and upgrade all.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["winget", "windows", "package", "install", "upgrade", "apps"];

    static string INeuronAgent.AgentInstructions => """
        You are Winget, the Windows package specialist. Install, upgrade, and search applications via winget.
        Run operations immediately and report exit code + output. Mutating installs/upgrades change the host —
        confirm intent before UpgradeAll/Install.
        """;

    [Description("List installed applications (winget list).")]
    Task<CommandResult> ListAsync(CancellationToken ct = default);

    [Description("Search the winget catalog for a query.")]
    Task<CommandResult> SearchAsync(string query, CancellationToken ct = default);

    [Description("Upgrade all upgradable applications (winget upgrade --all). Mutates the host.")]
    Task<CommandResult> UpgradeAllAsync(CancellationToken ct = default);

    [Description("Install an application by its winget package id. Mutates the host.")]
    Task<CommandResult> InstallAsync(string packageId, CancellationToken ct = default);
}
