using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
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
            [Description("Optional saved behavior local name to connect and activate after readiness succeeds")] string? behaviorName,
            CancellationToken token)
        {
            context.RequireActive();
            if (VerifiedActor.Current?.PrincipalId != principal)
            {
                throw new McpOperationException("GitHub setup requires the authenticated user.");
            }
            Guid? expectedRevision = null;
            if (AgentTurnContext.Current?.AllowedToolNames is not null)
            {
                if (AgentTurnContext.Current.SetupContinuation is not { ToolName: "connect_github_repository" } intent)
                {
                    throw new McpOperationException("The original GitHub setup intent is unavailable.");
                }
                repositoryUrl = intent.Scope;
                behaviorName = intent.BehaviorName;
                expectedRevision = intent.BehaviorRevision;
            }
            NeuronId? targetBehavior = null;
            if (!string.IsNullOrWhiteSpace(behaviorName))
            {
                targetBehavior = NeuronId.For<IBehavior>(context.Owner, PrincipalPartition.InstanceName(principal, behaviorName));
                var saved = await context.Requests.RequestAsync(targetBehavior.Value, new ReadBehavior(), token).ConfigureAwait(true);
                var revision = saved.Behavior.Draft?.Revision
                    ?? throw new McpOperationException("Save the behavior draft before connecting its repository.");
                if (AgentTurnContext.Current?.AllowedToolNames is not null && (expectedRevision is null || expectedRevision != revision))
                {
                    throw new McpOperationException("The behavior draft changed during authorization. Review the new draft and request connection again.");
                }
                expectedRevision = revision;
            }
            var state = await setup.ResolveAsync(context.Owner, principal, repositoryUrl, token).ConfigureAwait(true);
            if (state.State is "authentication_required" or "access_revoked")
            {
                var action = logins.Require(["connect_github_repository"], state.RepositoryUrl, token,
                    new SetupContinuation("connect_github_repository", state.RepositoryUrl, behaviorName, expectedRevision));
                return JsonSerializer.Serialize(new { state.State, state.RepositoryUrl, ActionId = action.Id,
                    Message = "Use the GitHub connection action. The saved request will resume after authorization." });
            }
            if (state.State == "ready" && state.Source is { } source && targetBehavior is { } behavior)
            {
                var latest = await context.Requests.RequestAsync(behavior, new ReadBehavior(), token).ConfigureAwait(true);
                if (latest.Behavior.Draft?.Revision != expectedRevision)
                {
                    throw new McpOperationException("The behavior draft changed while checking repository readiness. Review it before connecting.");
                }
                await context.Requests.SendAsync(behavior, new Subscribe(source, nameof(PullRequestChanged)), token).ConfigureAwait(true);
                if (AgentTurnContext.Current is { } turn)
                {
                    await context.Requests.SendAsync(turn.Chat, new Subscribe(behavior, "Note"), token).ConfigureAwait(true);
                }
                await context.Requests.RequestAsync(behavior, new EnableBehavior(expectedRevision), token).ConfigureAwait(true);
            }
            return JsonSerializer.Serialize(state);
        }
        return ValueTask.FromResult<IReadOnlyList<AITool>>([
            AIFunctionFactory.Create(Connect, new AIFunctionFactoryOptions
            {
                Name = "connect_github_repository",
                Description = "Resolve an authorized GitHub repository URL and verify required CI checks and webhook setup. If needed, offer browser connection. With a saved behavior name, complete its requested source/chat wiring and activation after readiness succeeds. Never asks for binding IDs or secrets.",
            })]);
    }
}
