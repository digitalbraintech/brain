namespace DigitalBrain.Abstractions.Entities;

// Live snapshot. Direct typed read/write. Not a graph endpoint: no journal, no synapses,
// not a Send target. Scripts mutate entities (IChart.Append); neurons fire signals.
[Alias("db.entity")]
public interface IEntity : IGrainWithStringKey
{
}

// Reads are the client-facing query surface (JOURNALS.md rule 3); writes stay behind each concrete contract's own entry-point opt-in.
[Alias("db.entity-state")]
public interface IEntity<TState> : IEntity
    where TState : class
{
    [Alias(nameof(Read))]
    Task<TState?> Read();
}
