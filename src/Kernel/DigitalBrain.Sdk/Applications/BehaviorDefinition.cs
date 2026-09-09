using System.Text.Json;

namespace DigitalBrain.Abstractions.Scripting;

public sealed class BehaviorDefinition
{
    private readonly ApplicationDefinition _application;
    private readonly SortedDictionary<string, string> _state = new(StringComparer.Ordinal);
    internal BehaviorDefinition(ApplicationDefinition application, string key) => (_application, Key) = (application, key);
    internal string Key { get; }
    internal string Schema => JsonSerializer.Serialize(_state);

    public StateKey<T> State<T>(string key, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        var schema = $"{ApplicationContracts.NameFor(typeof(T))}:state-v{schemaVersion}";
        if (!_state.TryAdd(key, schema))
        {
            throw new InvalidOperationException($"State '{key}' is already declared.");
        }
        return new(Key, key, schema);
    }

    public CommandPort<TRequest, TResult> Command<TRequest, TResult>(string key,
        Func<TRequest, ApplicationExecutionContext, CancellationToken, Task<TResult>> handler)
        => _application.StatefulCommand(this, key, handler);

    public QueryPort<TRequest, TResult> Query<TRequest, TResult>(string key,
        Func<TRequest, ApplicationQueryContext, CancellationToken, Task<TResult>> handler)
        => _application.Query(this, key, handler);

    public EventPort<T> Event<T>(string key) => _application.Event<T>(this, key);

    public InputPort<T> Handle<T>(string key,
        Func<T, ApplicationExecutionContext, CancellationToken, Task> handler)
        => _application.Input(this, key, handler);
}

public sealed class StateKey<T>
{
    internal StateKey(string scope, string key, string schema) => (Scope, Key, Schema) = (scope, key, schema);
    internal string Scope { get; }
    internal string Key { get; }
    internal string Schema { get; }
}

public sealed class ApplicationExecutionState
{
    private readonly string? _scope;
    private readonly Dictionary<string, string> _schema;
    private readonly Dictionary<string, string> _values;
    private readonly Dictionary<string, string> _writes = new(StringComparer.Ordinal);

    internal ApplicationExecutionState(string? scope, string? schema, Dictionary<string, string>? initial)
    {
        _scope = scope;
        _schema = schema is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(schema)!;
        _values = initial is null ? [] : new(initial, StringComparer.Ordinal);
    }

    public T Get<T>(StateKey<T> key)
    {
        Validate(key);
        return _values.TryGetValue(key.Key, out var json) ? JsonSerializer.Deserialize<T>(json)! : default!;
    }

    public void Set<T>(StateKey<T> key, T value)
    {
        Validate(key);
        var json = JsonSerializer.Serialize(value);
        _values[key.Key] = json;
        _writes[key.Key] = json;
    }

    internal Dictionary<string, string> PendingWrites => new(_writes, StringComparer.Ordinal);
    internal void Checkpointed() => _writes.Clear();

    private void Validate<T>(StateKey<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Scope != _scope || !_schema.TryGetValue(key.Key, out var schema) || schema != key.Schema)
        {
            throw new InvalidOperationException("This state key is not declared by the executing behavior.");
        }
    }
}
