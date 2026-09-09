using System.Text.Encodings.Web;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceOAuthHandler(
    IOptionsMonitor<OAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : OAuthHandler<OAuthOptions>(options, logger, encoder)
{
    protected override bool ValidateCorrelationId(AuthenticationProperties properties)
        => base.ValidateCorrelationId(properties)
            && BrowserLoginCorrelation.Claim(Context, properties, Context.RequestServices.GetRequiredService<SalesforceLogins>());
}
