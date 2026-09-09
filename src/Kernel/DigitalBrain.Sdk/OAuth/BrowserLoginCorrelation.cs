using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Sdk;

// Keep the framework's state, browser correlation and PKCE implementation. Add only a
// server-side one-use claim after correlation succeeds, before any authorization-code exchange.
public static class BrowserLoginCorrelation
{
    private static readonly object VerifiedRequestKey = new();

    public static bool Claim(HttpContext context, AuthenticationProperties properties, BrowserLogins logins)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(logins);
        if (!properties.Items.TryGetValue(BrowserLoginSurface.RequestKey, out var request) || !logins.TryClaim(request))
        {
            return false;
        }

        context.Items[VerifiedRequestKey] = request;
        return true;
    }

    // HttpContext survives exceptional framework failure paths where Properties is null.
    public static string? VerifiedRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(VerifiedRequestKey, out var request) ? request as string : null;
    }

    public static string? Scope(AuthenticationProperties? properties)
        => properties?.Items.TryGetValue(BrowserLoginSurface.ScopeKey, out var scope) == true ? scope : null;
}
