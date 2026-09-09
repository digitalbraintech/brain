using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DigitalBrain.Google;

internal static class GmailAuthentication
{
    private static readonly object TokensKey = new();

    internal static IServiceCollection AddGmailAuthentication(this IServiceCollection services, GmailOAuthConfiguration settings, BrowserLoginDefinition definition)
    {
        services.AddLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore.Authentication.OpenIdConnect", LogLevel.None);
            logging.AddFilter(typeof(GmailOAuthHandler).FullName, LogLevel.None);
            logging.AddFilter("Microsoft.IdentityModel", LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        });
        services.AddAuthentication().AddScheme<OpenIdConnectOptions, GmailOAuthHandler>(definition.Scheme, options =>
        {
            // Placeholders avoid startup failure; pre-auth guard refuses any challenge until configured.
            options.ClientId = settings.IsConfigured ? settings.ClientId : "not-configured";
            options.ClientSecret = settings.IsConfigured ? settings.ClientSecret : "not-configured";
            options.Authority = "https://accounts.google.com";
            options.CallbackPath = definition.CallbackPath;
            options.SignInScheme = "GmailNeverSignIn";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.RequireHttpsMetadata = true;
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
            options.BackchannelTimeout = TimeSpan.FromSeconds(30);
            options.BackchannelHttpHandler = new HttpClientHandler { AllowAutoRedirect = false };
            options.Scope.Clear();
            options.Scope.Add("openid"); options.Scope.Add("email"); options.Scope.Add(GmailOAuthConfiguration.ReadScope);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
                ValidateAudience = true,
                ValidAudience = options.ClientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            options.ProtocolValidator.RequireNonce = true;
            options.CorrelationCookie.HttpOnly = true;
            options.NonceCookie.HttpOnly = true;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            var secure = settings.IsConfigured && settings.PublicOrigin.Scheme == "https" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.CorrelationCookie.SecurePolicy = secure; options.NonceCookie.SecurePolicy = secure;
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    settings.RequireConfigured();
                    context.ProtocolMessage.RedirectUri = new Uri(settings.PublicOrigin, definition.CallbackPath).AbsoluteUri;
                    context.ProtocolMessage.SetParameter("include_granted_scopes", "true");
                    context.ProtocolMessage.SetParameter("access_type", "offline");
                    context.ProtocolMessage.Prompt = "consent"; // Guarantees refresh consent on reconnect/incremental access.
                    if (BrowserLoginCorrelation.Scope(context.Properties) == GmailLogins.ComposeScope)
                    {
                        context.ProtocolMessage.Scope += " " + GmailOAuthConfiguration.ComposeScope;
                    }

                    return Task.CompletedTask;
                },
                OnTokenResponseReceived = context =>
                {
                    if (!string.Equals(context.TokenEndpointResponse.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new McpOperationException("Google returned an unsupported token type.");
                    }
                    // HttpContext only. Never put access/refresh/ID tokens in tickets or AuthenticationProperties.
                    context.HttpContext.Items[TokensKey] = new Tokens(context.TokenEndpointResponse.AccessToken,
                        context.TokenEndpointResponse.RefreshToken, context.TokenEndpointResponse.Scope, context.TokenEndpointResponse.ExpiresIn);
                    return Task.CompletedTask;
                },
                OnTicketReceived = async context =>
                {
                    context.HandleResponse(); // Deliberately no sign-in handler or persistent authentication cookie.
                    var request = BrowserLoginCorrelation.VerifiedRequest(context.HttpContext);
                    var logins = context.HttpContext.RequestServices.GetRequiredService<GmailLogins>();
                    try
                    {
                        var principal = context.Principal;
                        var sub = principal?.FindFirst("sub")?.Value;
                        var email = principal?.FindFirst("email")?.Value;
                        if (request is null || string.IsNullOrWhiteSpace(sub) || sub.Length > 256
                            || string.IsNullOrWhiteSpace(email) || email.Length > 320
                            || !string.Equals(principal?.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                            || !context.HttpContext.Items.Remove(TokensKey, out var value) || value is not Tokens tokens)
                        {
                            throw new McpOperationException("Google identity validation was incomplete.");
                        }

                        var connections = context.HttpContext.RequestServices.GetRequiredService<GmailConnections>();
                        await logins.AcceptForActorAsync(request,
                            (actor, scope, valid) => connections.AcceptAsync(actor.Chat.Owner, actor.Actor.PrincipalId, sub, email, tokens.AccessToken,
                                tokens.RefreshToken, tokens.Scope, tokens.ExpiresIn, scope == GmailLogins.ComposeScope, valid,
                                context.HttpContext.RequestAborted)).ConfigureAwait(false);
                        await LoginPage.WriteAsync(context.HttpContext, "Gmail connected",
                            "You can close this tab and return to DigitalBrain. Login did not create a draft.", 200).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        logins.Reject(request);
                        await LoginPage.WriteAsync(context.HttpContext, "Gmail login could not be completed",
                            "Check required scopes and Google identity, then ask again in DigitalBrain.", 400).ConfigureAwait(false);
                    }
                    finally { context.HttpContext.Items.Remove(TokensKey); }
                },
                OnRemoteFailure = async context =>
                {
                    context.HandleResponse();
                    context.HttpContext.Items.Remove(TokensKey);
                    context.HttpContext.RequestServices.GetRequiredService<GmailLogins>().Reject(BrowserLoginCorrelation.VerifiedRequest(context.HttpContext));
                    await LoginPage.WriteAsync(context.HttpContext, "Gmail login failed or was cancelled",
                        "Return to DigitalBrain and check callback registration, account access and required scopes.", 400).ConfigureAwait(false);
                },
                OnAccessDenied = async context =>
                {
                    context.HandleResponse();
                    context.HttpContext.RequestServices.GetRequiredService<GmailLogins>().Reject(BrowserLoginCorrelation.VerifiedRequest(context.HttpContext));
                    await LoginPage.WriteAsync(context.HttpContext, "Gmail login was cancelled",
                        "No Gmail operation was performed.", 200).ConfigureAwait(false);
                },
            };
        });
        // AddScheme does not apply OpenIdConnect's essential post-configuration (state/data protection).
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>();
        return services;
    }

    private sealed class Tokens(string accessToken, string? refreshToken, string scope, string? expiresIn)
    {
        internal readonly string AccessToken = accessToken;
        internal readonly string? RefreshToken = refreshToken;
        internal readonly string Scope = scope;
        internal readonly string? ExpiresIn = expiresIn;
    }
}
