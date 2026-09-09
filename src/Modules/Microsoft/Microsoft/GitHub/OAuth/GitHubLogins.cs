using DigitalBrain.Sdk;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubLogins(GitHubOAuthConfiguration configuration, IServiceProvider services)
    : BrowserLogins(LoginDefinition, services)
{
    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "github", "GitHub", "GitHubIntegration", "/integrations/github/login", "/integrations/github/callback",
        "Connect the GitHub App to the requested repository. Credentials stay outside your scripts and conversation.")
    { RecoverPendingAfterRestart = true };
    protected override Uri? PublicOrigin => configuration.IsConfigured ? configuration.PublicOrigin : null;

    protected override async Task<string?> WaitBeforeResumeAsync(AgentTurnContext context, string? scope, CancellationToken cancellationToken)
    {
        if (scope is null) { return null; }
        using var actor = VerifiedActor.Enter(context.Actor);
        var state = await Services.GetRequiredService<IGitHubSetup>().ResolveAsync(context.Chat.Owner, context.Actor.PrincipalId, scope, cancellationToken).ConfigureAwait(false);
        return state.State == "webhook_verification_required"
            ? "GitHub is connected. Waiting for a signed webhook delivery at the configured public URL. The operator can send a GitHub App ping. Your saved behavior will resume after verification; its original draft remains unchanged."
            : null;
    }
}
