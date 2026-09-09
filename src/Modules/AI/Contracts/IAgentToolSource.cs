using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Modules contribute AI tools without the AI module referencing them (UI → AI is
// the allowed direction; this seam inverts tool ownership).
public interface IAgentToolSource
{
    ValueTask<IReadOnlyList<AITool>> GetToolsAsync(
        AgentToolContext context, CancellationToken cancellationToken);
}
