using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Salesforce;

internal static class SalesforceAuthentication
{
    internal static IServiceCollection AddSalesforceAuthentication(
        this IServiceCollection services, SalesforceOAuthConfiguration settings, BrowserLoginDefinition definition)
    {
        // Framework diagnostics can include remote error descriptions and callback query strings.
        // Keep only our sanitized status messages for this credential boundary.
        services.AddLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler", LogLevel.None);
            logging.AddFilter(typeof(SalesforceOAuthHandler).FullName, LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        });
        services.AddAuthentication()
            .AddCookie("SalesforceCallback", options => options.Cookie.Name = "db.sf.unused")
            .AddOAuth<OAuthOptions, SalesforceOAuthHandler>(definition.Scheme, options =>
            {
                settings.Configure(options);
                options.SignInScheme = "SalesforceCallback";
                options.CallbackPath = definition.CallbackPath;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.Scope.Clear();
                options.Scope.Add("mcp_api");
                options.Scope.Add("refresh_token");
                options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = settings.PublicOrigin.Scheme == "https"
                    ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                options.BackchannelHttpHandler = new HttpClientHandler { AllowAutoRedirect = false };
                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = async context =>
                    {
                        var request = BrowserLoginCorrelation.VerifiedRequest(context.HttpContext)
                            ?? throw new InvalidOperationException("Salesforce login is missing its original request.");
                        var issuer = context.Request.Query["iss"].ToString();
                        if (issuer.Length != 0 && issuer != "https://login.salesforce.com")
                        {
                            throw new InvalidOperationException("The Salesforce authorization issuer did not match.");
                        }
                        var connections = context.HttpContext.RequestServices.GetRequiredService<SalesforceConnections>();
                        await context.HttpContext.RequestServices.GetRequiredService<SalesforceLogins>().AcceptForActorAsync(request,
                            (turn, _, valid) => connections.StoreAsync(turn.Chat.Owner, turn.Actor.PrincipalId, context.AccessToken, context.RefreshToken,
                                context.ExpiresIn, valid, CancellationToken.None)).ConfigureAwait(false);
                    },
                    OnTicketReceived = async context =>
                    {
                        context.HandleResponse(); // Do not sign in or save tokens to a browser cookie.
                        // The login worker resumes the durable request even when the user closes
                        // this tab before the response finishes.
                        await LoginPage.WriteAsync(context.HttpContext, "Salesforce connected",
                            "You can close this tab. DigitalBrain is continuing your request.", 200).ConfigureAwait(false);
                    },
                    OnRemoteFailure = async context =>
                    {
                        context.HandleResponse();
                        context.HttpContext.RequestServices.GetRequiredService<SalesforceLogins>()
                            .Reject(BrowserLoginCorrelation.VerifiedRequest(context.HttpContext));
                        await LoginPage.WriteAsync(context.HttpContext, "Salesforce login was not completed",
                            "Return to DigitalBrain and ask again. Check the app callback, scopes and Salesforce permissions if this keeps happening.", 400)
                            .ConfigureAwait(false);
                    },
                    OnAccessDenied = async context =>
                    {
                        context.HandleResponse();
                        context.HttpContext.RequestServices.GetRequiredService<SalesforceLogins>()
                            .Reject(BrowserLoginCorrelation.VerifiedRequest(context.HttpContext));
                        await LoginPage.WriteAsync(context.HttpContext, "Salesforce login cancelled",
                            "No Salesforce operation was performed. You can close this tab.", 200).ConfigureAwait(false);
                    },
                };
            });
        return services;
    }
}
