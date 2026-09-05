using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Microsoft.GitHub;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubReviewTests
{
    [Fact]
    public void CI_gate_rejects_empty_missing_old_sha_wrong_producer_and_unapproved_conclusions()
    {
        var snapshot = Green();
        Assert.True(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build", 99)], ["success"]));
        Assert.True(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build", 99, "any")], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build", 100)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { ChecksComplete = false }, [new("build", 99)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { CiSha = "old" }, [new("build", 99)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("missing")], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build")], ["failure"]));
    }

    [Fact]
    public void Same_name_failures_cannot_be_hidden_by_a_green_producer_or_rerun()
    {
        var snapshot = Green();
        var check = snapshot.Checks[0];
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { Checks = [check, check with { AppId = 100, Conclusion = "failure" }] },
            [new("build", Kind: "any")], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { Checks = [check with { State = "in_progress", Conclusion = null }] },
            [new("build", 99)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { Checks = [check with { Conclusion = "neutral" }] },
            [new("build", 99)], ["success"]));
    }

    [Fact]
    public void Source_versions_invalidate_CI_reruns_without_repeating_completed_head_base()
    {
        IVersionedSignal original = new PullRequestChanged(Green(), PullRequestChange.Opened, "event1");
        IVersionedSignal rerun = new PullRequestChanged(Green() with { CiRevision = "rerun" }, PullRequestChange.Checks, "event2");
        Assert.Equal(original.SubjectKey, rerun.SubjectKey);
        Assert.NotEqual(original.Version, rerun.Version);
        Assert.Equal(original.CompletionKey, rerun.CompletionKey);
        IVersionedSignal pushed = new PullRequestChanged(Green() with { HeadSha = new string('c', 40), Revision = "new-head" }, PullRequestChange.Updated, "event3");
        Assert.NotEqual(original.CompletionKey, pushed.CompletionKey);
    }

    private static PullRequestSnapshot Green()
    {
        var snapshot = new GitHubFakeSource().Snapshot;
        return snapshot with { Checks = [new("build", 99, "check", "completed", "success", snapshot.HeadSha, "attempt-1", DateTimeOffset.UtcNow)] };
    }
}
