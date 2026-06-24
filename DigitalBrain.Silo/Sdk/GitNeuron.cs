using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// Typed git integration neuron. Re-homed from IAW's GitAgent onto MAIN's Neuron base; process-exec is delegated
// to the shared ProcessRunner. Reached by typed RPC; metrics are journal-derived (MAIN idiom).
[GrainType("digitalbrain.sdk.git.v1")]
public class GitNeuron : Neuron, IGitNeuron
{
    public GitNeuron(ILogger<GitNeuron> logger) : base(logger) { }

    public async Task<string> StatusAsync(string repoPath, CancellationToken ct = default)
        => (await ProcessRunner.RunAsync("git", "status", repoPath, ct: ct)).Output;

    public async Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        await ProcessRunner.RunAsync("git", "add -A", repoPath, ct: ct);
        var result = await ProcessRunner.RunAsync("git", $"commit -m \"{message.Replace("\"", "\\\"")}\"", repoPath, ct: ct);
        if (result.Succeeded)
            await FireAsync(new GitCommitted(repoPath, message));
        return result.Output;
    }

    public async Task<string> DiffAsync(string repoPath, CancellationToken ct = default)
        => (await ProcessRunner.RunAsync("git", "diff", repoPath, ct: ct)).Output;

    public async Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync("git", $"log --oneline -n {count}", repoPath, ct: ct);
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync("git", $"revert --no-edit {commitHash}", repoPath, ct: ct);
        if (result.Succeeded)
            await FireAsync(new GitReverted(repoPath, commitHash));
        return result.Output;
    }

    public Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var commits = OutgoingJournal.OfType<GitCommitted>().ToList();
        var reverts = OutgoingJournal.OfType<GitReverted>().Count();
        var last = commits.Count > 0 ? commits[^1].Timestamp : DateTimeOffset.MinValue;
        return Task.FromResult(new GitMetrics(commits.Count, reverts, last));
    }
}
