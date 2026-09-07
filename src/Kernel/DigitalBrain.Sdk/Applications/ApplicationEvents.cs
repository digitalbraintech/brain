using System.Text.Json;

namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationDefinition
{
    private readonly Dictionary<string, ApplicationEventOutput> _outputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApplicationEventConnection> _connections = new(StringComparer.Ordinal);

    internal EventPort<T> Event<T>(BehaviorDefinition behavior, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var output = new ApplicationEventOutput("application", KernelKey, behavior.Key, key, WireName(typeof(T)));
        if (!_outputs.TryAdd($"{behavior.Key}/{key}", output))
        {
            throw new InvalidOperationException($"Event '{behavior.Key}/{key}' is already declared.");
        }
        return new(ApplicationScopeId, output.SourceKind, output.SourceId, output.BehaviorKey, output.Key, output.Contract);
    }

    internal InputPort<T> Input<T>(BehaviorDefinition behavior, string key,
        Func<T, ApplicationExecutionContext, CancellationToken, Task> handler)
    {
        var command = StatefulCommand<T, bool>(behavior, key, async (value, context, ct) =>
        {
            await handler(value, context, ct).ConfigureAwait(false);
            return true;
        });
        _handlers[command.OperationKey] = _handlers[command.OperationKey] with
        {
            Declaration = _handlers[command.OperationKey].Declaration with { Kind = ApplicationOperationKind.Input },
        };
        return new(ApplicationScopeId, KernelKey, command.OperationKey, WireName(typeof(T)));
    }

    public void Connect<T>(string key, EventPort<T> source, InputPort<T> input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);
        if (source.ScopeId != ApplicationScopeId || input.ScopeId != ApplicationScopeId)
        {
            throw new InvalidOperationException("Connected ports must belong to this authenticated application scope.");
        }
        if (source.Contract != input.Contract) { throw new InvalidOperationException("Connected ports require the same wire contract."); }
        var descriptor = new ApplicationEventOutput(source.SourceKind, source.SourceId,
            source.BehaviorKey, source.Key, source.Contract);
        if (!_connections.TryAdd(key, new(key, descriptor, input.ApplicationKey, input.Operation, input.Contract,
                input.NeuronId?.ToString())))
        {
            throw new InvalidOperationException($"Connection '{key}' is already declared.");
        }
    }

    private string ApplicationScopeId
    {
        get
        {
            var key = _kernel.GetPrimaryKeyString().Split('/');
            return $"{key[0]}/{key[1]}";
        }
    }
}

public sealed partial class ApplicationExecutionContext
{
    public Task<ApplicationPublication> PublishAsync<T>(EventPort<T> output, T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.SourceKind != "application" || output.SourceId != _applicationKey
            || output.BehaviorKey != _behaviorKey)
        {
            throw new InvalidOperationException("This execution cannot publish another source's event.");
        }
        var payload = JsonSerializer.Serialize(value);
        var identity = JsonSerializer.Serialize(new[] { "publish", output.SourceKind, output.SourceId,
            output.BehaviorKey, output.Key, output.Contract, payload });
        return CheckpointAsync<ApplicationPublication>(identity, revision: null,
            (effect, ct) => _catalog.Publish(new(effect.OperationId, OperationId, output.SourceKind,
                output.SourceId, Revision, output.BehaviorKey, output.Key, output.Contract, payload)).WaitAsync(ct),
            cancellationToken);
    }
}
