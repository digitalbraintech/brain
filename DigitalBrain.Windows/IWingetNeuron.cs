using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Windows;

public interface IWingetNeuron : IAgent
{
    static string IAgent.AgentDisplayName => "Winget";

    static string IAgent.AgentDescription =>
        "Manage Windows applications via winget: list installed, search, install, and upgrade all.";

    static string[] IAgent.AgentCapabilities =>
        ["winget", "windows", "package", "install", "upgrade", "apps"];

    static string IAgent.AgentInstructions => """
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
