using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Microsoft.Hosting;

public static class GitHubHostingExtensions
{
    /// <summary>Configures one fixed GitHub App repository and private webhook endpoint on the kernel.</summary>
    public static DigitalBrainModuleBuilder<MicrosoftModule> WithGitHubRepository(
        this DigitalBrainModuleBuilder<MicrosoftModule> module,
        string bindingId, string owner, Guid principal, long appId, long installationId,
        long repositoryId, string repositoryOwner, string repositoryName,
        string? endpointId = null, Uri? apiHost = null, Uri? mcpEndpoint = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        if (principal == Guid.Empty || appId < 1 || installationId < 1 || repositoryId < 1)
        {
            throw new ArgumentException("A fixed principal, GitHub App, installation and numeric repository identity are required.");
        }
        if (bindingId.Length > 80 || bindingId.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("The GitHub hosting binding must use at most 80 letters, digits or hyphens.", nameof(bindingId));
        }
        var state = module.Brain.GetOrAddState(static _ => new GitHubHostingState(), out var added);
        if (added)
        {
            module.AddProjection(state);
        }
        state.Add(bindingId, endpointId ?? bindingId, new GitHubProjection(module.Brain, bindingId, owner, principal, appId,
            installationId, repositoryId, repositoryOwner, repositoryName, endpointId ?? bindingId, apiHost, mcpEndpoint));
        return module;
    }

    private sealed class GitHubHostingState : DigitalBrainModuleProjection
    {
        private readonly Dictionary<string, GitHubProjection> _repositories = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _endpoints = new(StringComparer.Ordinal);

        internal void Add(string id, string endpoint, GitHubProjection projection)
        {
            if (_repositories.Count >= 32 || _repositories.ContainsKey(id) || !_endpoints.Add(endpoint))
            {
                throw new InvalidOperationException("Configure at most 32 GitHub repositories with unique binding and endpoint identities.");
            }
            _repositories.Add(id, projection);
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            foreach (var projection in _repositories.Values)
            {
                projection.Apply(builder);
            }
        }
    }

    private sealed class GitHubProjection(DigitalBrainBuilder brain, string id, string owner, Guid principal,
        long appId, long installationId, long repositoryId, string repoOwner, string repoName,
        string endpointId, Uri? apiHost, Uri? mcpEndpoint) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ParameterResource>? _privateKey;
        private IResourceBuilder<ParameterResource>? _webhookSecret;

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            if (brain.FakesEnabled)
            {
                return;
            }
            _privateKey ??= brain.ApplicationBuilder.AddParameter($"github-{id}-app-private-key", secret: true)
                .WithDescription("PEM private key for the configured GitHub App. Only the kernel receives this secret.");
            _webhookSecret ??= brain.ApplicationBuilder.AddParameter($"github-{id}-webhook-secret", secret: true)
                .WithDescription($"GitHub webhook HMAC secret (at least 16 characters). Forward only /integrations/github/{endpointId}/webhook through HTTPS.");
            var root = $"DigitalBrain:Microsoft:GitHub:Repositories:{id}";
            builder
                .WithEnvironment(EnvironmentKeys.For(root, "Owner"), owner)
                .WithEnvironment(EnvironmentKeys.For(root, "Principal"), principal.ToString("D"))
                .WithEnvironment(EnvironmentKeys.For(root, "AppId"), appId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithEnvironment(EnvironmentKeys.For(root, "InstallationId"), installationId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithEnvironment(EnvironmentKeys.For(root, "RepositoryId"), repositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithEnvironment(EnvironmentKeys.For(root, "RepoOwner"), repoOwner)
                .WithEnvironment(EnvironmentKeys.For(root, "RepoName"), repoName)
                .WithEnvironment(EnvironmentKeys.For(root, "EndpointId"), endpointId)
                .WithEnvironment(EnvironmentKeys.For(root, "PrivateKeyPem"), _privateKey)
                .WithEnvironment(EnvironmentKeys.For(root, "WebhookSecret"), _webhookSecret);
            if (apiHost is not null)
            {
                builder.WithEnvironment(EnvironmentKeys.For(root, "ApiHost"), apiHost.AbsoluteUri);
            }
            if (mcpEndpoint is not null)
            {
                builder.WithEnvironment(EnvironmentKeys.For(root, "McpEndpoint"), mcpEndpoint.AbsoluteUri);
            }
        }
    }
}
