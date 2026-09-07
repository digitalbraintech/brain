using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationExecutionContext
{
    public async Task<ArmedEventWait<T>> BeginWaitAsync<T>(EventPort<T> source,
        CancellationToken cancellationToken = default)
    {
        ValidateWaitSource(source);
        ApplicationRunScope.ThrowIfDefinitionEffect("wait for an event");
        if (Interlocked.Exchange(ref _calling, 1) != 0)
        {
            throw new InvalidOperationException("Concurrent calls require separate execution branches.");
        }
        try
        {
            var ordinal = _ordinal++;
            var identity = WaitIdentity(source);
            await _kernel.ArmEventWait(OperationId, _claim.Lease, ordinal, identity,
                    source.SourceKind, source.SourceId, source.BehaviorKey, source.Key, source.Contract,
                    State.PendingWrites)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            State.Checkpointed();
            return new(this, ordinal, identity);
        }
        finally
        {
            Volatile.Write(ref _calling, 0);
        }
    }

    public async Task<T> WaitForAsync<T>(EventPort<T> source, CancellationToken cancellationToken = default)
        => await (await BeginWaitAsync(source, cancellationToken).ConfigureAwait(false))
            .ResultAsync(cancellationToken).ConfigureAwait(false);

    internal async Task<T> ResolveWaitAsync<T>(int ordinal, string identity, CancellationToken cancellationToken)
    {
        var awaitIdentity = JsonSerializer.Serialize(new[]
        {
            "await-event", ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), identity,
        });
        return await CheckpointAsync<T>(awaitIdentity, revision: null, async (_, token) =>
        {
            var payload = await _kernel.AwaitEventWait(
                    OperationId, _claim.Lease, ordinal, identity)
                .WaitAsync(token).ConfigureAwait(false);
            if (payload is null) { throw new ApplicationExecutionSuspendedException(); }
            return JsonSerializer.Deserialize<T>(payload)!;
        }, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateWaitSource<T>(EventPort<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = _kernel.GetPrimaryKeyString().Split('/');
        if (source.ScopeId != $"{key[0]}/{key[1]}" || source.SourceKind is not ("neuron" or "application") ||
            source.Contract != ApplicationContracts.NameFor(typeof(T)))
        {
            throw new InvalidOperationException("An event wait requires an owned, typed neuron event source.");
        }
    }

    private static string WaitIdentity<T>(EventPort<T> source)
    {
        var identity = JsonSerializer.Serialize(new[]
        {
            "wait-event", source.SourceId, source.BehaviorKey, source.Key, source.Contract,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}

public sealed class ArmedEventWait<T>
{
    private readonly ApplicationExecutionContext context;
    private readonly int ordinal;
    private readonly string identity;

    internal ArmedEventWait(ApplicationExecutionContext context, int ordinal, string identity)
    {
        this.context = context;
        this.ordinal = ordinal;
        this.identity = identity;
    }

    public Task<T> ResultAsync(CancellationToken cancellationToken = default)
        => context.ResolveWaitAsync<T>(ordinal, identity, cancellationToken);
}
