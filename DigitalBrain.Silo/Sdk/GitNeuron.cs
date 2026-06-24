using System.Diagnostics;
using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// Typed git integration neuron. Body re-homed from IAW's GitAgent process-exec mechanics onto MAIN's
// Neuron : DurableGrain base (dropping the Agent<T>/IChatClient/State plumbing). Reached by typed RPC.
[GrainType("digitalbrain.sdk.git.v1")]
public class GitNeuron : Neuron, IGitNeuron
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    public GitNeuron(ILogger<GitNeuron> logger) : base(logger) { }

    public async Task<string> StatusAsync(string repoPath, CancellationToken ct = default)
        => (await RunGitAsync("status", repoPath, ct)).Output;

    public async Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        await RunGitAsync("add -A", repoPath, ct);
        var (output, exitCode) = await RunGitAsync($"commit -m \"{message.Replace("\"", "\\\"")}\"", repoPath, ct);
        if (exitCode == 0)
            await FireAsync(new GitCommitted(repoPath, message));
        return output;
    }

    public async Task<string> DiffAsync(string repoPath, CancellationToken ct = default)
        => (await RunGitAsync("diff", repoPath, ct)).Output;

    public async Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default)
    {
        var (output, _) = await RunGitAsync($"log --oneline -n {count}", repoPath, ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default)
    {
        var (output, exitCode) = await RunGitAsync($"revert --no-edit {commitHash}", repoPath, ct);
        if (exitCode == 0)
            await FireAsync(new GitReverted(repoPath, commitHash));
        return output;
    }

    public Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var commits = OutgoingJournal.OfType<GitCommitted>().ToList();
        var reverts = OutgoingJournal.OfType<GitReverted>().Count();
        var last = commits.Count > 0 ? commits[^1].Timestamp : DateTimeOffset.MinValue;
        return Task.FromResult(new GitMetrics(commits.Count, reverts, last));
    }

    // A non-zero git exit code is legitimate data (conflict, nothing-to-commit), returned to the caller.
    // Only a failure to START the process is an error — surfaced, never swallowed (fail-fast policy).
    private async Task<(string Output, int ExitCode)> RunGitAsync(string arguments, string repoPath, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);
        var token = timeoutCts.Token;

        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start 'git {arguments}' in '{repoPath}'.");

        try
        {
            var outTask = process.StandardOutput.ReadToEndAsync(token);
            var errTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            var output = await outTask;
            var error = await errTask;
            var combined = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
            return (combined, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process, arguments);
            throw;
        }
    }

    private void KillProcessTree(Process process, string arguments)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            // Benign race: the process may exit between the HasExited check and Kill. Log, do not swallow silently.
            Logger.LogWarning(ex, "Failed to kill timed-out 'git {Args}' process.", arguments);
        }
    }
}
