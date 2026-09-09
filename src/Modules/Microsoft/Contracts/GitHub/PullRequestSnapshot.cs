namespace DigitalBrain.Microsoft.GitHub;

/// <summary>Authoritative observed PR and CI state. Incomplete evidence never grants a review.</summary>
[GenerateSerializer, Alias("db.github.pull-request-snapshot")]
public sealed record PullRequestSnapshot(
    [property: Id(0)] int Number,
    [property: Id(1)] string Title,
    [property: Id(2)] string Url,
    [property: Id(3)] bool IsOpen,
    [property: Id(4)] bool IsDraft,
    [property: Id(5)] string HeadSha,
    [property: Id(6)] string BaseSha,
    [property: Id(7)] string? MergeSha,
    [property: Id(8)] string CiSha,
    [property: Id(9)] GitHubCheck[] Checks,
    [property: Id(10)] bool ChecksComplete,
    [property: Id(11)] DateTimeOffset ObservedAt,
    [property: Id(12)] DateTimeOffset CreatedAt,
    [property: Id(13)] string Revision,
    [property: Id(14)] string CiRevision,
    [property: Id(15)] long RepositoryId = 0,
    [property: Id(16)] string? BaseBranch = null);

[GenerateSerializer, Alias("db.github.check")]
public sealed record GitHubCheck(
    [property: Id(0)] string Name,
    [property: Id(1)] long? AppId,
    [property: Id(2)] string Kind,
    [property: Id(3)] string State,
    [property: Id(4)] string? Conclusion,
    [property: Id(5)] string Sha,
    [property: Id(6)] string AttemptId,
    [property: Id(7)] DateTimeOffset UpdatedAt);

[GenerateSerializer, Alias("db.github.review-evidence")]
public sealed record GitHubReviewEvidence(
    [property: Id(0)] string HeadSha,
    [property: Id(1)] string BaseSha,
    [property: Id(2)] string Text,
    [property: Id(3)] string Hash,
    [property: Id(4)] bool Complete);
