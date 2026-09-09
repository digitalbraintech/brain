using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.UI;

// Same wall as IChart: Read() is the client-facing query via IEntity<TState>;
// Render stays a same-silo grain call driven by the kit tools.
[Alias("ui.graph")]
public interface IGraph : IEntity<GraphState>
{
    [Alias(nameof(Render))]
    Task Render(GraphState state);
}
