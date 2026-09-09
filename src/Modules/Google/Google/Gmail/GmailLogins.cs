using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;

namespace DigitalBrain.Google;

internal sealed class GmailLogins(GmailOAuthConfiguration configuration, GmailConnections connections, IServiceProvider services)
    : BrowserLogins(LoginDefinition, services)
{
    internal const string ComposeScope = "compose";
    internal static readonly string[] ReadTools = ["get_current_account", "search_threads", "get_thread", "list_labels"];

    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "gmail",
        "Gmail",
        "GmailIntegration",
        "/integrations/gmail/login",
        "/integrations/gmail/callback",
        "Sign in with Google to connect Gmail. Credentials stay outside the conversation. Login never creates a draft.");

    protected override Uri? PublicOrigin => configuration.IsConfigured ? configuration.PublicOrigin : null;

    protected override string? GetConnectionRevision(AgentTurnContext context)
    {
        try { return connections.Identity(context.Chat.Owner, context.Actor.PrincipalId).Revision.ToString("N"); }
        catch (McpAuthenticationRequiredException) { return null; }
    }

    // A compose login escalates the scope and never resumes the interrupted read.
    internal UserActionRequest RequireLogin(bool compose, CancellationToken cancellationToken = default)
        => Require(compose ? [] : ReadTools, compose ? ComposeScope : null, cancellationToken);
}
