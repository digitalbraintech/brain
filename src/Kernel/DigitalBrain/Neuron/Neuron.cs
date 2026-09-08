using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// A durable actor with one receive slot. Owns its synapses, two bounded journals, and the
// latest signal of each type it received. Fire travels along synapses; nothing else routes.
public abstract class Neuron : DurableGrain, INeuron, INeuronQuery
{
    // Latest-per-type is keyed by type name, so a caller putting identity in the type would
    // grow it without bound. The cap turns that mistake into one sentence of advice.
    public const int MaxSignalTypesPerNeuron = 256;

    private readonly NeuronActivationComponents _components;
    private SignalDelivery? _handling;

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _components = runtime.Bind(ServiceProvider, Id);
    }

    public NeuronId Id => NeuronId.FromGrainId(this.GetGrainId());

    protected TimeProvider TimeProvider => _components.Clock;

    // The delivery this turn is reacting to, or null outside Deliver.
    protected SignalDelivery? CurrentDelivery => _handling;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);
    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Override to react. The default neuron does nothing: the signal is already journaled and remembered.
    protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;

    // ---- INeuron ----

    public Task<FireOutcome> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default)
        => FireAsync(signal, to, correlation, cancellationToken);

    public async Task Connect(NeuronId target, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Connect(target, signalType))
        {
            await WriteStateAsync().ConfigureAwait(true);
        }
    }

    public async Task Disconnect(NeuronId target, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Disconnect(target, signalType))
        {
            await WriteStateAsync().ConfigureAwait(true);
        }
    }

    public async Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (_components.Latest.Count >= MaxSignalTypesPerNeuron && !_components.Latest.ContainsKey(delivery.Signal.Type))
        {
            throw new SignalRejectedException(
                $"Neuron '{Id}' already remembers {MaxSignalTypesPerNeuron} signal types. "
                + "Type names are vocabulary such as 'Note'; put identity in the neuron name.");
        }

        _components.Journals.AppendIncoming(delivery);
        _components.Latest[delivery.Signal.Type] = delivery;
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var previous = _handling;
        _handling = delivery;
        try
        {
            await ReceiveAsync(delivery, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _handling = previous;
        }
    }

    // ---- INeuronQuery ----

    public Task<IReadOnlyList<SignalDelivery>> ReadState()
        => Task.FromResult<IReadOnlyList<SignalDelivery>>(
            [.. _components.Latest.Values.OrderBy(d => d.Signal.Type, StringComparer.Ordinal)]);

    public Task<IReadOnlyList<Synapse>> ReadSynapses() => Task.FromResult(_components.Synapses.All());

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_components.Journals.Read(kind, afterSequence));

    // ---- for subclasses ----

    protected async Task<FireOutcome> FireAsync(
        Signal signal,
        NeuronId? to = null,
        CorrelationId? correlation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        // Re-validate: a Signal deserialized from the wire may bypass Create. Keep the
        // normalized instance — Create fills a blank body with "{}".
        signal = Signal.Create(signal.Type, signal.Body);

        if (to is { } target && target == Id)
        {
            throw new SignalRejectedException($"Neuron '{Id}' cannot fire at itself.");
        }

        // The directed edge is created before the journal entry so anatomy and traffic agree.
        if (to is { } single)
        {
            _components.Synapses.Connect(single, signal.Type);
        }

        var delivery = SignalDelivery.Create(signal, Id, _components.Journals.OutgoingNextSequence, TimeProvider, _handling, correlation);
        _components.Journals.AppendOutgoing(delivery);
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var targets = to is { } one
            ? [one]
            : _components.Synapses.ForType(signal.Type).Select(s => s.Target).Where(t => t != Id).Distinct().ToArray();

        List<Exception>? failures = null;
        foreach (var receiver in targets)
        {
            try
            {
                using var path = NeuronRequestPath.Enter(Id, receiver);
                await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId())
                    .Deliver(delivery, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                $"Delivery of '{signal.Type}' from '{Id}' failed for {failures.Count} of {targets.Length} receivers.",
                failures);
        }

        return new FireOutcome(delivery.SignalId, delivery.CorrelationId, targets.Length);
    }

    // The obsolete RegisterTimer interleaved with other grain calls by default. Neurons
    // require serialized turns; use ScheduleTurn (RegisterGrainTimer, Interleave = false).
    protected new IDisposable RegisterTimer(Func<object, Task> callback, object state, TimeSpan dueTime, TimeSpan period)
        => throw new InvalidOperationException($"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns. Use {nameof(ScheduleTurn)}.");

    // Next serialized turn after the current Receive returns. Orleans RegisterGrainTimer with
    // Interleave = false is a grain call, not an interleaved callback.
    protected IGrainTimer ScheduleTurn(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return this.RegisterGrainTimer(
            static (work, cancellation) => work(cancellation),
            callback,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.Zero,
                Period = Timeout.InfiniteTimeSpan,
                Interleave = false,
                KeepAlive = true,
            });
    }
}
