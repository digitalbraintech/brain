using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Microsoft.GitHub;

/// <summary>Authenticated connection setup; URLs are resolved to canonical source identities.</summary>
public interface IGitHubSetup
{
    Task<GitHubSetupResult> ResolveAsync(OwnerId owner, PrincipalId principal, string repositoryUrl, CancellationToken cancellationToken = default);
    Task<GitHubSetupResult> ConnectAsync(OwnerId owner, PrincipalId principal, GitHubRepositoryAccess access, CancellationToken cancellationToken = default);
}

public sealed record GitHubRepositoryAccess(long AppId, long InstallationId, long RepositoryId, string RepositoryOwner, string RepositoryName);
public sealed record GitHubSetupResult(string RepositoryUrl, string State, NeuronId? Source,
    GitHubCheckRequirement[] RequiredChecks, string Detail, string? WebhookUrl = null);
