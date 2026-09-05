using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Microsoft.Hosting;

public static class GitHubAppHostingExtensions
{
    /// <summary>Projects one reusable GitHub App to the kernel. Repository access is connected by each signed-in principal.</summary>
    public static DigitalBrainModuleBuilder<MicrosoftModule> WithGitHubApp(
        this DigitalBrainModuleBuilder<MicrosoftModule> module,
        long appId, string slug, string clientId, Uri publicOrigin, Uri publicWebhookUrl)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentOutOfRangeException.ThrowIfLessThan(appId, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (!publicOrigin.IsAbsoluteUri || publicOrigin.UserInfo.Length != 0 || publicOrigin.AbsolutePath != "/"
            || publicOrigin.Query.Length != 0 || publicOrigin.Fragment.Length != 0
            || publicOrigin.Scheme != "https" && !(publicOrigin.Scheme == "http" && publicOrigin.IsLoopback))
        {
            throw new ArgumentException("GitHub OAuth requires a public HTTPS origin or a local development origin.", nameof(publicOrigin));
        }
        if (!publicWebhookUrl.IsAbsoluteUri || publicWebhookUrl.Scheme != "https" || publicWebhookUrl.UserInfo.Length != 0
            || publicWebhookUrl.Query.Length != 0 || publicWebhookUrl.Fragment.Length != 0
            || publicWebhookUrl.AbsolutePath != "/integrations/github/webhook")
        {
            throw new ArgumentException("GitHub requires a public HTTPS /integrations/github/webhook endpoint.", nameof(publicWebhookUrl));
        }
        var projection = module.Brain.GetOrAddState(_ => new GitHubAppProjection(module.Brain, appId, slug, clientId, publicOrigin, publicWebhookUrl), out var added);
        if (!added)
        {
            throw new InvalidOperationException("Configure the shared GitHub App once per DigitalBrain.");
        }
        module.AddProjection(projection);
        return module;
    }

    private sealed class GitHubAppProjection(DigitalBrainBuilder brain, long appId, string slug, string clientId, Uri publicOrigin, Uri publicWebhookUrl)
        : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ParameterResource>? _privateKey;
        private IResourceBuilder<ParameterResource>? _webhookSecret;
        private IResourceBuilder<ParameterResource>? _clientSecret;

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            if (brain.FakesEnabled)
            {
                return;
            }
            _privateKey ??= brain.ApplicationBuilder.AddParameter("github-app-private-key", secret: true)
                .WithDescription("PEM private key for the GitHub App; projected only to the kernel.");
            _webhookSecret ??= brain.ApplicationBuilder.AddParameter("github-app-webhook-secret", secret: true)
                .WithDescription("GitHub App HMAC webhook secret, at least 16 characters.");
            _clientSecret ??= brain.ApplicationBuilder.AddParameter("github-app-client-secret", secret: true)
                .WithDescription("GitHub App OAuth client secret; user tokens are only used during authorization.");
            const string root = "DigitalBrain:Microsoft:GitHub:App";
            builder.WithEnvironment(EnvironmentKeys.For(root, "AppId"), appId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithEnvironment(EnvironmentKeys.For(root, "Slug"), slug)
                .WithEnvironment(EnvironmentKeys.For(root, "ClientId"), clientId)
                .WithEnvironment(EnvironmentKeys.For(root, "PublicOrigin"), publicOrigin.AbsoluteUri)
                .WithEnvironment(EnvironmentKeys.For(root, "PublicWebhookUrl"), publicWebhookUrl.AbsoluteUri)
                .WithEnvironment(EnvironmentKeys.For(root, "PrivateKeyPem"), _privateKey)
                .WithEnvironment(EnvironmentKeys.For(root, "WebhookSecret"), _webhookSecret)
                .WithEnvironment(EnvironmentKeys.For(root, "ClientSecret"), _clientSecret);
        }
    }
}
