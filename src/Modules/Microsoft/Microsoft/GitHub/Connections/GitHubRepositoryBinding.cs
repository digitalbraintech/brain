using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;
// Configuration is trusted application input. Never serialize this type or include it in a log.
internal sealed class GitHubRepositoryBinding
{
    private int _revoked;
    private int _recovering;
    internal GitHubRepositoryBinding(string id, OwnerId owner, PrincipalId principal, long repositoryId, long installationId, long appId, string repoOwner, string repoName, string privateKeyPem, string webhookSecret, string? endpointId = null, Uri? apiHost = null, Uri? mcpEndpoint = null, string? authorizationEpoch = null)
    {
        ValidateName(id);
        ValidateName(endpointId ?? id);
        ValidateName(repoOwner);
        ValidateName(repoName);
        ArgumentOutOfRangeException.ThrowIfLessThan(repositoryId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(installationId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(appId, 1);
        if (principal.Value == Guid.Empty || string.IsNullOrWhiteSpace(privateKeyPem) || webhookSecret.Length < 16)
        {
            throw new InvalidOperationException("GitHub requires a principal, App private key and a webhook secret of at least 16 characters.");
        }

        Id = id;
        Owner = owner;
        Principal = principal;
        RepositoryId = repositoryId;
        InstallationId = installationId;
        AppId = appId;
        RepoOwner = repoOwner;
        RepoName = repoName;
        PrivateKeyPem = privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal);
        WebhookSecret = webhookSecret;
        EndpointId = endpointId ?? id;
        var validatedApi = ValidateEndpoint(apiHost ?? new Uri("https://api.github.com/"));
        ApiHost = new Uri(validatedApi.AbsoluteUri.TrimEnd('/') + '/');
        McpEndpoint = ValidateEndpoint(mcpEndpoint ?? new Uri("https://api.githubcopilot.com/mcp/"));
        LocalName = $"github-{Id}-{RepositoryId}";
        InstanceName = PrincipalPartition.InstanceName(Principal, LocalName);
        Revision = authorizationEpoch ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}|{Owner}|{Principal}|{RepositoryId}|{InstallationId}|{AppId}|{RepoOwner}|{RepoName}|{ApiHost}|{McpEndpoint}|{privateKeyPem}")));
    }

    public string Id { get; }
    public OwnerId Owner { get; }
    public PrincipalId Principal { get; }
    public long RepositoryId { get; }
    public long InstallationId { get; }
    public long AppId { get; }
    public string RepoOwner { get; }
    public string RepoName { get; }
    public Uri ApiHost { get; }
    public Uri McpEndpoint { get; }
    public string EndpointId { get; }
    public string LocalName { get; }
    public string InstanceName { get; }
    public string Revision { get; }
    public bool Enabled => Volatile.Read(ref _revoked) == 0 && RecoveryComplete;
    internal bool RecoveryComplete => Volatile.Read(ref _recovering) == 0;
    internal string PrivateKeyPem { get; }
    internal string WebhookSecret { get; }
    internal string RepositoryPath => $"repos/{Uri.EscapeDataString(RepoOwner)}/{Uri.EscapeDataString(RepoName)}";

    public void Revoke() => Interlocked.Exchange(ref _revoked, 1);
    internal void BeginRecovery() => Interlocked.Exchange(ref _recovering, 1);
    internal void CompleteRecovery() => Interlocked.Exchange(ref _recovering, 0);
    internal void Authorize(OwnerId owner, PrincipalId principal)
    {
        if (!Enabled || Owner != owner || Principal != principal || VerifiedActor.Current?.PrincipalId != Principal)
        {
            throw new McpOperationException("The configured GitHub repository is not available to this principal.", McpFailureKind.AccessDenied);
        }
    }

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value is "." or ".." || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidOperationException("GitHub binding and repository names must use letters, digits, '.', '-' or '_'.");
        }
    }

    private static Uri ValidateEndpoint(Uri value)
    {
        if (!value.IsAbsoluteUri || value.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(value.UserInfo) || !string.IsNullOrEmpty(value.Query) || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new InvalidOperationException("GitHub endpoints must be absolute HTTPS URLs without embedded credentials, queries or fragments.");
        }

        return value;
    }
}

internal sealed class GitHubRepositoryBindings
{
    internal const string ConfigurationRoot = "DigitalBrain:Microsoft:GitHub:Repositories";
    private readonly ConcurrentDictionary<string, GitHubRepositoryBinding> _bindings;
    internal GitHubRepositoryBindings(IEnumerable<GitHubRepositoryBinding> bindings)
    {
        _bindings = new(bindings.ToDictionary(static binding => binding.Id, StringComparer.Ordinal), StringComparer.Ordinal);
        if (_bindings.Count > 32 || _bindings.Values.Select(static b => b.EndpointId).Distinct(StringComparer.Ordinal).Count() != _bindings.Count || _bindings.Values.Select(static b => (b.Owner, b.InstanceName)).Distinct().Count() != _bindings.Count)
        {
            throw new InvalidOperationException("Configure at most 32 GitHub bindings with unique endpoint and neuron identities.");
        }
    }

    public IReadOnlyCollection<GitHubRepositoryBinding> All => _bindings.Values.ToArray();

    internal void Add(GitHubRepositoryBinding binding)
    {
        if (_bindings.Count >= 256 && !_bindings.ContainsKey(binding.Id))
        {
            throw new McpOperationException("The GitHub connection capacity is full.", McpFailureKind.Capacity);
        }

        _bindings.AddOrUpdate(binding.Id, binding, (_, previous) =>
        {
            if (previous.Revision == binding.Revision)
            {
                return previous;
            }

            previous.Revoke();
            return binding;
        });
    }

    internal GitHubRepositoryBinding? FindByRepository(OwnerId owner, PrincipalId principal, string repoOwner, string repoName) => _bindings.Values.FirstOrDefault(binding => binding.Owner == owner && binding.Principal == principal && string.Equals(binding.RepoOwner, repoOwner, StringComparison.OrdinalIgnoreCase) && string.Equals(binding.RepoName, repoName, StringComparison.OrdinalIgnoreCase));
    public GitHubRepositoryBinding? Find(string id) => _bindings.GetValueOrDefault(id);
    public bool TryFor(NeuronId neuron, [NotNullWhen(true)] out GitHubRepositoryBinding? binding)
    {
        binding = _bindings.Values.FirstOrDefault(candidate => neuron.Type == "repository" && candidate.Owner == neuron.Owner && candidate.InstanceName == neuron.Name);
        return binding is not null;
    }

    public GitHubRepositoryBinding GetFor(NeuronId neuron)
    {
        if (!TryFor(neuron, out var binding))
        {
            throw new McpOperationException("The GitHub repository neuron is not bound to a configured repository.", McpFailureKind.AccessDenied);
        }

        binding.Authorize(neuron.Owner, VerifiedActor.Current?.PrincipalId ?? default);
        return binding;
    }

    public static GitHubRepositoryBindings Read(IConfiguration configuration) => new(configuration.GetSection(ConfigurationRoot).GetChildren().Select(static section => new GitHubRepositoryBinding(section.Key, new OwnerId(Required(section, "Owner")), new PrincipalId(Guid.Parse(Required(section, "Principal"))), long.Parse(Required(section, "RepositoryId"), System.Globalization.CultureInfo.InvariantCulture), long.Parse(Required(section, "InstallationId"), System.Globalization.CultureInfo.InvariantCulture), long.Parse(Required(section, "AppId"), System.Globalization.CultureInfo.InvariantCulture), Required(section, "RepoOwner"), Required(section, "RepoName"), Required(section, "PrivateKeyPem"), Required(section, "WebhookSecret"), section["EndpointId"], section["ApiHost"] is { } api ? new Uri(api) : null, section["McpEndpoint"] is { } mcp ? new Uri(mcp) : null)));
    private static string Required(IConfiguration section, string name) => string.IsNullOrWhiteSpace(section[name]) ? throw new InvalidOperationException($"GitHub binding configuration requires {name}.") : section[name]!;
}
