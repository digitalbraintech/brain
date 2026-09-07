namespace DigitalBrain.Abstractions.Scripting;

public sealed class QueryPort<TRequest, TResult>
{
    internal QueryPort(CommandPort<TRequest, TResult> operation) => Operation = operation;
    internal CommandPort<TRequest, TResult> Operation { get; }
    public Task<TResult> QueryAsync(TRequest request, CancellationToken cancellationToken = default)
        => Operation.InvokeAsync(request, cancellationToken);
}

public sealed class ApplicationQueryContext
{
    internal ApplicationQueryContext(ApplicationExecutionContext execution)
        => (State, OperationId, Revision) = (new(execution.State), execution.OperationId, execution.Revision);
    public ApplicationReadOnlyState State { get; }
    public Guid OperationId { get; }
    public string Revision { get; }
}

public sealed class ApplicationReadOnlyState
{
    private readonly ApplicationExecutionState _state;
    internal ApplicationReadOnlyState(ApplicationExecutionState state) => _state = state;
    public T Get<T>(StateKey<T> key) => _state.Get(key);
}

public sealed partial class ApplicationDefinition
{
    internal QueryPort<TRequest, TResult> Query<TRequest, TResult>(BehaviorDefinition behavior, string key,
        Func<TRequest, ApplicationQueryContext, CancellationToken, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var command = StatefulCommand<TRequest, TResult>(behavior, key,
            (value, execution, ct) => handler(value, new(execution), ct));
        var operation = $"{behavior.Key}/{key}";
        var registered = _handlers[operation];
        _handlers[operation] = registered with { Declaration = registered.Declaration with { IsQuery = true } };
        return new(command);
    }
}

public sealed partial class ApplicationExecutionContext
{
    public Task<TResult> QueryAsync<TRequest, TResult>(QueryPort<TRequest, TResult> query,
        TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeAsync(query.Operation, request, cancellationToken);
    }
}
