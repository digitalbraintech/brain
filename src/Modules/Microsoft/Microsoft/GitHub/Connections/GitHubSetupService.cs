using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubSetupService(IConfiguration configuration, GitHubRepositoryBindings bindings, IGrainFactory grains, GitHubInstallationTokens tokens, IGitHubRepositorySource source) : IGitHubSetup
{
    internal const string AppRoot = "DigitalBrain:Microsoft:GitHub:App";
    private readonly SemaphoreSlim _restore = new(1, 1);
    private bool _restored;
    internal async Task RestoreAsync(CancellationToken token)
    {
        await _restore.WaitAsync(token);
        try
        {
            if (_restored)
            {
                return;
            }

            foreach (var saved in await grains.GetGrain<IGitHubConnectionRegistry>("github").ReadAsync().WaitAsync(token))
            {
                if (!Configured(saved.AppId))
                {
                    continue;
                }

                var binding = Create(saved);
                binding.BeginRecovery();
                bindings.Add(binding);
            }

            _restored = true;
        }
        finally
        {
            _restore.Release();
        }
    }

    public async Task<GitHubSetupResult> ResolveAsync(OwnerId owner, PrincipalId principal, string repositoryUrl, CancellationToken cancellationToken = default)
    {
        RequirePrincipal(principal);
        var coordinates = ParseUrl(repositoryUrl);
        var url = $"https://github.com/{coordinates.Owner}/{coordinates.Name}";
        await RestoreAsync(cancellationToken);
        var binding = bindings.FindByRepository(owner, principal, coordinates.Owner, coordinates.Name);
        if (binding is null)
        {
            return new(url, Configured(null) ? "authentication_required" : "operator_setup_required", null, [], Configured(null) ? "Connect GitHub and select repository access to resume this request." : "The operator must configure the GitHub App id, private key, webhook secret and OAuth callback before connection.");
        }

        var id = NeuronId.For<IRepository>(owner, binding.InstanceName);
        // Activating the source restores its durable revocation before exposing access.
        await grains.GetGrain<IRepositoryProjection>(id.ToGrainId()).ReadProjectionAsync().WaitAsync(cancellationToken);
        if (!binding.Enabled)
        {
            return new(url, "access_revoked", id, [], "Reconnect repository access before enabling subscriptions.");
        }

        var ingress = configuration[$"{AppRoot}:PublicWebhookUrl"];
        if (!Uri.TryCreate(ingress, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https"
            || endpoint.AbsolutePath != "/integrations/github/webhook" || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            return new(url, "webhook_setup_required", id, [], "Configure a public HTTPS URL for /integrations/github/webhook. Successful sign-in does not establish inbound reachability.");
        }

        var proof = await grains.GetGrain<IRepositoryProjection>(id.ToGrainId()).ReadLastWebhookAsync().WaitAsync(cancellationToken);
        if (proof is null)
        {
            return new(url, "webhook_verification_required", id, [], "Repository access is connected. Send a GitHub App ping or redeliver an event to verify authenticated inbound delivery. Required CI checks are validated after delivery is verified.", ingress);
        }

        RequiredChecksRead checks;
        try
        {
            checks = await source.GetRequiredChecksAsync(binding, null, cancellationToken);
        }
        catch (McpOperationException error)
        {
            return new(url, error.Kind == McpFailureKind.AccessDenied ? "access_denied" : "unavailable", id, [], "GitHub readiness could not be established. Verify repository access and try again.");
        }

        if (!checks.Complete || checks.Checks.Length == 0)
        {
            return new(url, "ci_setup_required", id, checks.Checks, checks.Detail ?? "Select an explicit nonempty set of required CI checks.", ingress);
        }

        return new(url, "ready", id, checks.Checks, "Repository access, required CI checks and authenticated webhook delivery are validated.", ingress);
    }

    public async Task<GitHubSetupResult> ConnectAsync(OwnerId owner, PrincipalId principal, GitHubRepositoryAccess access, CancellationToken cancellationToken = default)
    {
        RequirePrincipal(principal);
        if (!Configured(access.AppId))
        {
            throw new McpOperationException("GitHub App operator setup is incomplete or this installation belongs to a different App.");
        }

        await RestoreAsync(cancellationToken);
        var prior = bindings.FindByRepository(owner, principal, access.RepositoryOwner, access.RepositoryName);
        var identity = prior?.Id ?? $"r-{access.RepositoryId}-{GitHubRepositorySource.Hash(owner + ":" + principal)[..16]}";
        var record = new GitHubConnectionRecord(identity, owner, principal, access.AppId, access.InstallationId, access.RepositoryId, access.RepositoryOwner, access.RepositoryName, prior is { Enabled: true } && prior.InstallationId == access.InstallationId ? prior.Revision : Guid.NewGuid().ToString("N"));
        var binding = Create(record);
        // Installation-token exchange verifies the App can actually read this numeric repository.
        _ = await tokens.GetTokenAsync(binding, true, cancellationToken);
        await grains.GetGrain<IGitHubConnectionRegistry>("github").StoreAsync(record).WaitAsync(cancellationToken);
        bindings.Add(binding);
        return await ResolveAsync(owner, principal, $"https://github.com/{access.RepositoryOwner}/{access.RepositoryName}", cancellationToken);
    }

    private bool Configured(long? appId) => long.TryParse(configuration[$"{AppRoot}:AppId"], out var configured) && configured > 0 && (appId is null || appId == configured) && !string.IsNullOrWhiteSpace(configuration[$"{AppRoot}:PrivateKeyPem"]) && configuration[$"{AppRoot}:WebhookSecret"] is { Length: >= 16 };
    private GitHubRepositoryBinding Create(GitHubConnectionRecord record) => new(record.Id, record.Owner, record.Principal, record.RepositoryId, record.InstallationId, record.AppId, record.RepositoryOwner, record.RepositoryName, configuration[$"{AppRoot}:PrivateKeyPem"]!, configuration[$"{AppRoot}:WebhookSecret"]!, authorizationEpoch: record.Epoch);
    private static void RequirePrincipal(PrincipalId principal)
    {
        if (VerifiedActor.Current?.PrincipalId != principal)
        {
            throw new UnauthorizedAccessException("GitHub setup requires the authenticated principal.");
        }
    }

    internal static (string Owner, string Name) ParseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new McpOperationException("Use the HTTPS GitHub repository URL, for example https://github.com/intochat/digitalbrain.");
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 || part.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
        {
            throw new McpOperationException("Use a repository URL with an owner and repository name.");
        }

        return (parts[0], parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1]);
    }
}

internal sealed class GitHubConnectionRecovery(GitHubSetupService setup, GitHubRepositoryBindings bindings, IGrainFactory grains, IHostApplicationLifetime lifetime, ILogger<GitHubConnectionRecovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => ready.TrySetResult());
        await ready.Task.WaitAsync(stoppingToken);
        await setup.RestoreAsync(stoppingToken);
        foreach (var binding in bindings.All)
        {
            try
            {
                using var actor = VerifiedActor.Enter(new(binding.Principal, "github-recovery"));
                await grains.GetGrain<IRepositoryProjection>(NeuronId.For<IRepository>(binding.Owner, binding.InstanceName).ToGrainId()).ReadProjectionAsync().WaitAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("GitHub connection recovery deferred after {FailureType}.", error.GetType().Name);
            }
        }
    }
}
