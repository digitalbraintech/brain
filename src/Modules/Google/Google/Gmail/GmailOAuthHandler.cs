using System.Text.Encodings.Web;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Google;

internal sealed class GmailOAuthHandler(IOptionsMonitor<OpenIdConnectOptions> options, ILoggerFactory logger, HtmlEncoder htmlEncoder, UrlEncoder encoder)
    : OpenIdConnectHandler(options, logger, htmlEncoder, encoder)
{
    protected override bool ValidateCorrelationId(AuthenticationProperties properties)
        => base.ValidateCorrelationId(properties)
            && BrowserLoginCorrelation.Claim(Context, properties, Context.RequestServices.GetRequiredService<GmailLogins>());
}
