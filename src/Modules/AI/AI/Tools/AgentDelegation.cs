using System.ComponentModel;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// One adapter for every specialist. Registration provides identity and purpose,
// while the model supplies only a question, never a source/owner/grain key.
public sealed class AgentDelegation<TAgent>(
    string name,
    string description,
    string localInstanceName,
    OwnerId? allowedOwner = null) : IAgentToolSource
    where TAgent : IAgent
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireActive();
        if (context.Principal is not { } principal
            || (allowedOwner is { } owner && owner != context.Owner))
        {
            return ValueTask.FromResult<IReadOnlyList<AITool>>([]);
        }

        var instance = PrincipalPartition.InstanceName(principal, localInstanceName);
        async Task<string> Ask(
            [Description("The question or investigation to delegate, including relevant context.")] string request,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request);
            if (request.Length > 16000)
            {
                throw new ArgumentException("The delegated request exceeds 16000 characters.", nameof(request));
            }

            var reply = await context.Requests.RequestAsync<TAgent>(instance, new AgentRequest(request), cancellationToken)
                .ConfigureAwait(true);
            return reply.Text;
        }

        return ValueTask.FromResult<IReadOnlyList<AITool>>(
            [AIFunctionFactory.Create(Ask, new AIFunctionFactoryOptions { Name = name, Description = description })]);
    }
}
