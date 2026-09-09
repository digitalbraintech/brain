using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("graph")]
internal sealed class GraphEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GraphState> state)
    : Entity<GraphState>(state), IGraph
{
    public async Task Render(GraphState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await SaveAsync(state);
    }
}
