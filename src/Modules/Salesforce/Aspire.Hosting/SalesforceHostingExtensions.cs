using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public static class SalesforceHostingExtensions
{
    // Only the kernel receives these values. The endpoint defaults to the hosted sobject server;
    // the public origin defaults to the kernel's own http endpoint.
    public static DigitalBrainModuleBuilder<SalesforceModule> WithHostedMcp(
        this DigitalBrainModuleBuilder<SalesforceModule> module,
        Uri? endpoint = null,
        Uri? publicOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var state = module.Brain.GetOrAddState(static brain => new SalesforceHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        state.Enable(endpoint, publicOrigin);
        return module;
    }

    private sealed class SalesforceHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private const string OAuthRoot = SalesforceModule.OAuthConfigurationRoot;

        private bool _enabled;
        private Uri? _endpoint;
        private Uri? _publicOrigin;
        private IResourceBuilder<ParameterResource>? _consumerKey;
        private IResourceBuilder<ParameterResource>? _consumerSecret;

        internal void Enable(Uri? endpoint, Uri? publicOrigin)
        {
            _enabled = true;
            _endpoint = endpoint ?? _endpoint;
            _publicOrigin = publicOrigin ?? _publicOrigin;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled || brain.FakesEnabled)
            {
                return;
            }

            _consumerKey ??= brain.ApplicationBuilder.AddParameter("salesforce-consumer-key", secret: true)
                .WithDescription(
                    "Consumer key (client ID) from your existing Salesforce "
                    + "[External Client App](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html). "
                    + "Register http://localhost:5080/integrations/salesforce/callback, enable PKCE and JWT access tokens, and allow mcp_api and refresh_token.",
                    enableMarkdown: true);
            _consumerSecret ??= brain.ApplicationBuilder.AddParameter("salesforce-consumer-secret", secret: true)
                .WithDescription(
                    "Consumer secret from the same Salesforce External Client App. Enable Require Secret for Web Server Flow. "
                    + "Only the kernel receives this secret; Salesforce login happens in your browser when the assistant needs access.",
                    enableMarkdown: true);

            builder
                .WithEnvironment(SalesforceModule.McpEndpointEnvironmentVariable, (_endpoint ?? SalesforceModule.DefaultMcpEndpoint).AbsoluteUri)
                .WithEnvironment(EnvironmentKeys.For(OAuthRoot, "ConsumerKey"), _consumerKey)
                .WithEnvironment(EnvironmentKeys.For(OAuthRoot, "ConsumerSecret"), _consumerSecret);
            if (_publicOrigin is { } origin)
            {
                builder.WithEnvironment(EnvironmentKeys.For(OAuthRoot, "PublicOrigin"), origin.AbsoluteUri);
            }
            else
            {
                builder.WithEnvironment(EnvironmentKeys.For(OAuthRoot, "PublicOrigin"), builder.GetEndpoint("http"));
            }
        }
    }
}
