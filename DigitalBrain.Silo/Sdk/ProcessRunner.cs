using System.Diagnostics;
using System.Text;
using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// Shared process-exec core for the SDK integration neurons (Shell/Git/DotNet/NuGet/Winget). Harvests IAW
// ShellAgent's mechanics: timeout + kill-tree, command block-list, base64 PowerShell, output truncation.
// A pure static runner returning a typed CommandResult — no Agent base, no DI, no per-call grain state.
public static class ProcessRunner
{
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "format", "shutdown", "reboot", "mkfs", "dd", "fdisk", "diskpart"
    };

    private static readonly string[] BlockedArgumentPatterns =
    [
        "rm -rf /", "del /s /q c:\\", ":(){ :|:& };:"
    ];

    // Run a binary directly (no shell). A non-zero exit is data; a failure to START throws (fail-fast).
    public static async Task<CommandResult> RunAsync(
        string fileName, string arguments, string? workingDirectory = null,
        int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName} {arguments}'.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            stopwatch.Stop();
            return new CommandResult(process.ExitCode, Truncate(await outputTask), Truncate(await errorTask), stopwatch.Elapsed);
        }
        // Only the timeout is caught; caller cancellation propagates (fail-fast).
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            KillTree(process);
            stopwatch.Stop();
            return new CommandResult(-1, "", $"Process '{fileName}' timed out after {timeoutMs} ms.", stopwatch.Elapsed);
        }
    }

    // Run a command line through the OS shell (cmd.exe / bash). Block-listed commands are rejected, not run.
    public static Task<CommandResult> ShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var blocked = Validate(command);
        if (blocked is not null)
            return Task.FromResult(new CommandResult(-1, "", blocked, TimeSpan.Zero));

        var (shell, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c {command}")
            : ("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"");
        return RunAsync(shell, args, workingDirectory, timeoutMs, ct);
    }

    // Run a PowerShell command, base64-encoded to avoid quoting issues. Block-listed commands are rejected.
    public static Task<CommandResult> PowerShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var blocked = Validate(command);
        if (blocked is not null)
            return Task.FromResult(new CommandResult(-1, "", blocked, TimeSpan.Zero));

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        return RunAsync(shell, $"-NoProfile -NonInteractive -EncodedCommand {encoded}", workingDirectory, timeoutMs, ct);
    }

    private static string? Validate(string command)
    {
        var firstToken = command.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var commandName = Path.GetFileNameWithoutExtension(firstToken);
        if (BlockedCommands.Contains(commandName))
            return $"Command blocked: '{commandName}' is prohibited.";
        foreach (var pattern in BlockedArgumentPatterns)
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return "Command blocked: contains a prohibited pattern.";
        return null;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Benign race: the process exited between the HasExited check and Kill.
        }
    }

    private static string Truncate(string output, int maxLength = 16_384)
    {
        if (output.Length <= maxLength) return output;
        var head = maxLength * 2 / 3;
        var tail = maxLength / 3;
        return $"{output[..head]}\n\n... [{output.Length - maxLength} chars truncated] ...\n\n{output[^tail..]}";
    }
}
