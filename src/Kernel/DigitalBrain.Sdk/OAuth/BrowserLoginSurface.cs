using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Sdk;

// The two exact login and callback paths carry their own one-use request capability and OAuth
// correlation checks, so the kernel maps this surface before its authentication gate.
public sealed class BrowserLoginSurface(BrowserLogins logins) : IHttpSurface
{
    public const string RequestKey = "login-request";
    public const string ScopeKey = "login-scope";

    public void Map(IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var definition = logins.Definition;
        app.Use(async (context, next) =>
        {
            var login = context.Request.Path.Equals(definition.LoginPath, StringComparison.Ordinal);
            if (!login && !context.Request.Path.Equals(definition.CallbackPath, StringComparison.Ordinal))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            LoginPage.PrivateHeaders(context);
            Redact(context);
            if (logins.ConfiguredOrigin is not { } origin)
            {
                await LoginPage.WriteAsync(context, $"{definition.DisplayName} setup is incomplete",
                    "Configure the provider's OAuth client privately in Aspire, then ask again in DigitalBrain.", 503).ConfigureAwait(false);
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method)
                || !string.Equals(context.Request.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.Request.Host.Value, origin.Authority, StringComparison.OrdinalIgnoreCase))
            {
                await LoginPage.WriteAsync(context, $"Invalid {definition.DisplayName} login request",
                    "Open the login action from DigitalBrain.", 400).ConfigureAwait(false);
                return;
            }

            try
            {
                if (login)
                {
                    var request = context.Request.Query["request"];
                    if (context.Request.Query.Count != 1 || request.Count != 1 || !logins.TryBegin(request[0], out var scope))
                    {
                        await LoginPage.WriteAsync(context, "This login link expired or was already opened",
                            $"Return to DigitalBrain and request {definition.DisplayName} access again.", 410).ConfigureAwait(false);
                        return;
                    }

                    var properties = new AuthenticationProperties();
                    properties.Items[RequestKey] = request[0];
                    if (scope is not null)
                    {
                        properties.Items[ScopeKey] = scope;
                    }

                    await context.ChallengeAsync(definition.Scheme, properties).ConfigureAwait(false);
                    return;
                }

                await next(context).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await LoginPage.WriteAsync(context, $"{definition.DisplayName} authorization is unavailable",
                    "Check the private OAuth configuration and try a new request.", 503).ConfigureAwait(false);
            }
            finally
            {
                Redact(context);
            }
        });
    }

    // Callback query strings carry authorization codes; traces keep only the path.
    private static void Redact(HttpContext context)
    {
        var activity = Activity.Current;
        activity?.SetTag("url.query", null);
        activity?.SetTag("http.target", context.Request.Path.Value);
        activity?.SetTag("url.full", $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}");
    }
}
