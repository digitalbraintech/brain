using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Windows;

// Typed shell/PowerShell execution. Re-homed from IAW's IShell/ShellAgent onto Neuron; the process mechanics
// (timeout, kill-tree, block-list, base64 pwsh, output truncation) live in the shared ProcessRunner.
public interface IShellNeuron : IAgent
{
    static string IAgent.AgentDisplayName => "Shell";

    static string IAgent.AgentDescription =>
        "Executes shell and PowerShell commands with timeout enforcement, output capture, and a safety block-list.";

    static string[] IAgent.AgentCapabilities =>
        ["execute", "shell", "powershell", "command", "script", "process"];

    static string IAgent.AgentInstructions => """
        You are Shell, the command execution specialist. You run CLI commands, scripts, and non-.NET tools.

        RULES:
        - Execute commands immediately — never tell the user to run them manually.
        - Default 120-second timeout; processes exceeding it are killed (whole tree).
        - Report exit code, stdout, stderr, duration.
        - Dangerous commands (format, shutdown, rm -rf /) are blocked.
        - Use RunDotnet for dotnet CLI commands; prefer the DotNet neuron for build/test/run.
        """;

    [Description("Execute a shell command (cmd.exe on Windows, bash on Linux). Returns exit code, stdout, stderr, duration.")]
    Task<CommandResult> ExecuteAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default);

    [Description("Run a dotnet CLI command. Returns exit code, stdout, stderr, duration.")]
    Task<CommandResult> RunDotnetAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default);

    [Description("Execute a PowerShell command (base64-encoded to avoid quoting issues).")]
    Task<CommandResult> ExecutePowerShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default);
}
