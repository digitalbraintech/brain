using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubSetupTools(IGitHubSetup setup, GitHubLogins logins) : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        if (context.Principal is not { } principal || context.Agent.Type != "assistant")
        {
            return ValueTask.FromResult<IReadOnlyList<AITool>>([]);
        }
        async Task<string> Connect(
            [Description("HTTPS GitHub repository URL, for example https://github.com/intochat/digitalbrain")] string repositoryUrl,
            CancellationToken token)
        {
            context.RequireActive();
            if (VerifiedActor.Current?.PrincipalId != principal)
            {
                throw new McpOperationException("GitHub setup requires the authenticated user.");
            }
            if (AgentTurnContext.Current?.AllowedToolNames is not null)
            {
                if (AgentTurnContext.Current.SetupContinuation is not { ToolName: "connect_github_repository" } intent)
                {
                    throw new McpOperationException("The original GitHub setup intent is unavailable.");
                }
                repositoryUrl = intent.Scope;
            }
            var state = await setup.ResolveAsync(context.Owner, principal, repositoryUrl, token).ConfigureAwait(true);
            if (state.State is "authentication_required" or "access_revoked")
            {
                var action = logins.Require(["connect_github_repository"], state.RepositoryUrl, token,
                    new SetupContinuation("connect_github_repository", state.RepositoryUrl));
                return JsonSerializer.Serialize(new { state.State, state.RepositoryUrl, ActionId = action.Id,
                    Message = "Use the GitHub connection action. The saved request will resume after authorization." });
            }
            return JsonSerializer.Serialize(state);
        }
        return ValueTask.FromResult<IReadOnlyList<AITool>>([
            AIFunctionFactory.Create(Connect, new AIFunctionFactoryOptions
            {
                Name = "connect_github_repository",
                Description = "Resolve an authorized GitHub repository URL and verify required CI checks and webhook setup. If needed, offer browser connection. Application event wiring is authored and activated through the application tools. Never asks for binding IDs or secrets.",
            })]);
    }
}
