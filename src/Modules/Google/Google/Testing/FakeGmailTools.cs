using DigitalBrain.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Google;

// Fixture tools are prepared on the real Gmail neuron, not a service replacing IGmail.
internal sealed class FakeGmailTools : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GmailTools.RequireIdentity(context);
        return ValueTask.FromResult<IReadOnlyList<AITool>>([
            AIFunctionFactory.Create((string query, int pageSize = 10) =>
            {
                context.RequireActive();
                return """{"untrustedData":true,"threads":[{"id":"thread-intochat","messages":[{"id":"message-intochat","subject":"New Customer","snippet":"Please send company information.","sender":"vlad@intochat.io"}]}]}""";
            }, new AIFunctionFactoryOptions { Name = "search_threads", Description = "Search fixture Gmail threads." }),
        ]);
    }
}
