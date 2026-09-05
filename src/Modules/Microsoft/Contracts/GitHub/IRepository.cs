using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Sdk.Webhooks;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github-repository")]
public interface IRepository : IWebhook, IHandle<ReadPullRequest>, IHandle<ReadPullRequests>,
    IHandle<ReadReviewEvidence>, IHandle<ReadRequiredChecks>;

[GenerateSerializer, Alias("github.read-pull-request")]
public sealed record ReadPullRequest([property: Id(0)] int Number,
    [property: Id(1)] bool Refresh = false) : Signal<PullRequestRead>, IDeferredReply;

[GenerateSerializer, Alias("github.pull-request-read")]
public sealed record PullRequestRead(
    [property: Id(0)] PullRequestSnapshot? Snapshot,
    [property: Id(1)] bool Available) : Signal;

[GenerateSerializer, Alias("github.read-pull-requests")]
public sealed record ReadPullRequests : Signal<PullRequestsRead>;

[GenerateSerializer, Alias("github.pull-requests-read")]
public sealed record PullRequestsRead(
    [property: Id(0)] PullRequestSnapshot[] Snapshots,
    [property: Id(1)] bool Available) : Signal;

[GenerateSerializer, Alias("github.repository-access-revoked")]
public sealed record RepositoryAccessRevoked(
    [property: Id(0)] string BindingId,
    [property: Id(1)] NeuronId Repository,
    [property: Id(2)] string EventId) : Signal;

[Flags]
public enum PullRequestChange { Opened = 1, Updated = 2, Closed = 4, Checks = 8 }

[GenerateSerializer, Alias("github.pull-request-changed")]
public sealed record PullRequestChanged(
    [property: Id(0)] PullRequestSnapshot Snapshot,
    [property: Id(1)] PullRequestChange Change,
    [property: Id(2)] string EventId) : Signal, IVersionedSignal
{
    public string SubjectKey => $"github:{Snapshot.RepositoryId}:{Snapshot.Number}";
    public string Version => $"{Snapshot.Revision}:{Snapshot.CiRevision}";
    public string CompletionKey => $"{Snapshot.HeadSha}:{Snapshot.BaseSha}";
    public DateTimeOffset? CreatedAt => Snapshot.CreatedAt;
}

[GenerateSerializer, Alias("github.read-review-evidence")]
public sealed record ReadReviewEvidence([property: Id(0)] PullRequestSnapshot Expected) : Signal<ReviewEvidenceRead>, IDeferredReply;

[GenerateSerializer, Alias("github.review-evidence-read")]
public sealed record ReviewEvidenceRead([property: Id(0)] PullRequestSnapshot Current,
    [property: Id(1)] GitHubReviewEvidence? Evidence, [property: Id(2)] string? Detail = null) : Signal;

[GenerateSerializer, Alias("github.read-required-checks")]
public sealed record ReadRequiredChecks([property: Id(0)] string? Branch = null) : Signal<RequiredChecksRead>, IDeferredReply;

[GenerateSerializer, Alias("github.required-checks-read")]
public sealed record RequiredChecksRead([property: Id(0)] GitHubCheckRequirement[] Checks,
    [property: Id(1)] bool Complete, [property: Id(2)] string? Detail = null) : Signal;
