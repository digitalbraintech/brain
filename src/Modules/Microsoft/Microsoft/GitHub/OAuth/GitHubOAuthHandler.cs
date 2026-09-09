using System.Text.Encodings.Web;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubOAuthHandler(IOptionsMonitor<OAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : OAuthHandler<OAuthOptions>(options, logger, encoder)
{
    // This scheme completes provider connections; it does not authenticate ordinary
    // kernel requests, even when ASP.NET selects the only scheme as its default.
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override bool ValidateCorrelationId(AuthenticationProperties properties)
        => base.ValidateCorrelationId(properties)
            && BrowserLoginCorrelation.Claim(Context, properties, Context.RequestServices.GetRequiredService<GitHubLogins>());
}
