using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceLogins(SalesforceOAuthConfiguration configuration, SalesforceConnections connections, IServiceProvider services)
    : BrowserLogins(LoginDefinition, services)
{
    internal static readonly string[] ReadTools = ["getUserInfo", "soqlQuery"];

    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "salesforce",
        "Salesforce",
        "SalesforceIntegration",
        "/integrations/salesforce/login",
        "/integrations/salesforce/callback",
        "Log in to Salesforce to continue this request. Your credentials stay outside the conversation.");

    protected override Uri? PublicOrigin => configuration.PublicOrigin;

    protected override string? GetConnectionRevision(AgentTurnContext context)
    {
        try { return connections.Identity(context.Chat.Owner, context.Actor.PrincipalId).Revision; }
        catch (McpAuthenticationRequiredException) { return null; }
    }

    // A read resumes after login; a write needs a fresh preview and explicit confirmation.
    internal UserActionRequest RequireLogin(bool readOnly, CancellationToken cancellationToken = default)
        => Require(readOnly ? ReadTools : [], null, cancellationToken);
}
