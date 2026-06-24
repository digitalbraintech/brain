using System.ComponentModel;

namespace DigitalBrain.Protocol;

// Typed git integration neuron. Re-homed from IAW's IGit/GitAgent onto MAIN's Neuron model.
// Dispatch is typed grain-method RPC (zero-reflection) — infra calls are request/response and do not
// need the synapse/journal causality story. Metrics ARE journal-derived (MAIN idiom): each successful
// commit/revert fires a typed synapse, and GetMetricsAsync reads them back from the outgoing journal.
public interface IGitNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "Git";

    static string INeuronAgent.AgentDescription =>
        "Manages git version control operations: status, commit, diff, log, revert.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["git", "commit", "diff", "log", "revert", "version-control", "repository"];

    static string INeuronAgent.AgentInstructions => """
        You are Git, the version control specialist. You manage commits, diffs, log, and revert.

        RULES:
        - Execute git operations immediately — never give manual instructions.
        - Always run Status before Commit to verify staged changes.
        - Write commit messages in imperative mood, max 72 characters for the subject.
        - Never force-push or rewrite public history.
        - Do NOT modify file contents — that is the FileSystem neuron's job.
        """;

    [Description("Show git status of a repository. Returns branch name, staged/unstaged/untracked files.")]
    Task<string> StatusAsync(string repoPath, CancellationToken ct = default);

    [Description("Stage all changes and create a commit with a message. Returns git output.")]
    Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default);

    [Description("Show git diff of unstaged changes. Returns file paths and line changes.")]
    Task<string> DiffAsync(string repoPath, CancellationToken ct = default);

    [Description("Show recent commits (oneline). Default 10 entries.")]
    Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default);

    [Description("Revert a specific commit by hash (no-edit). Returns git output.")]
    Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default);

    [Description("Journal-derived metrics: total successful commits/reverts and the last commit time.")]
    Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record GitMetrics(
    [property: Id(0)] int TotalCommits,
    [property: Id(1)] int TotalReverts,
    [property: Id(2)] DateTimeOffset LastCommit);

[GenerateSerializer]
public record GitCommitted(string RepoPath, string Message)
    : Synapse(nameof(GitCommitted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record GitReverted(string RepoPath, string CommitHash)
    : Synapse(nameof(GitReverted), DateTimeOffset.UtcNow);
