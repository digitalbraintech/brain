using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// Concrete subclasses must redeclare [PersistentState(...)] on their own constructor parameter
// and forward to base(state) — Orleans binds facets on the leaf class's own constructor, not an
// inherited one. Omitting it compiles but throws at activation.
public abstract class Entity<TState> : Grain, IEntity<TState>
    where TState : class
{
    private readonly IPersistentState<TState> _state;

    protected Entity(
        [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TState> state)
    {
        _state = state;
    }

    protected TState? State => _state.RecordExists ? _state.State : null;

    public Task<TState?> Read() => Task.FromResult(State);

    protected async Task SaveAsync(TState value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        _state.State = value;
        await _state.WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }
}
