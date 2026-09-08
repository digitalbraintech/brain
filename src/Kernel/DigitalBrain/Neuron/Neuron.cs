using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// A durable actor with one receive slot. Owns its synapses, two bounded journals, and the
// latest signal of each type it received. Fire travels along synapses; nothing else routes.
public abstract class Neuron : DurableGrain, INeuron, INeuronInbox
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

    // The delivery this turn is reacting to, or null outside ReceiveAsync.
    protected SignalDelivery? CurrentDelivery => _handling;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);

        // Entries accepted before the last deactivation are still pending: resume the drain.
        if (_components.Reacted.Value < _components.Journals.IncomingLastSequence)
        {
            Wake();
        }
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

        Wake();
    }

    // ---- INeuronInbox ----

    // Reacts to exactly one pending entry per call, then re-wakes if more remain, so that
    // accepts interleave with reactions and journal order is preserved.
    async Task INeuronInbox.Drain()
    {
        var next = _components.Reacted.Value + 1;
        if (next > _components.Journals.IncomingLastSequence)
        {
            return;
        }

        if (!_components.Journals.TryReadIncoming(next, out var delivery))
        {
            // Fell out of the retained window before we reacted: count it as lost and move on.
            DrainTelemetry.Lost(Logger, Id, next);
            await AdvanceAsync(next).ConfigureAwait(true);
            return;
        }

        var previous = _handling;
        _handling = delivery;
        try
        {
            await ReceiveAsync(delivery, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception failure)
        {
            // The cursor stays, so the entry is not lost, and nothing else happens: the
            // journal is the only schedule. The next Deliver or activation retries it.
            DrainTelemetry.Failed(Logger, Id, next, failure);
            return;
        }
        finally
        {
            _handling = previous;
        }

        await AdvanceAsync(next).ConfigureAwait(true);
    }

    // ---- INeuron: the reads ----

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

    // ---- the drain's wake-ups ----

    private ILogger? Logger => ServiceProvider.GetService<ILogger<Neuron>>();

    // A one-way call to ourselves: it returns immediately and is queued behind the current turn.
    private void Wake() => GrainFactory.GetGrain<INeuronInbox>(this.GetGrainId()).Drain().Ignore();

    private async Task AdvanceAsync(long sequence)
    {
        _components.Reacted.Value = sequence;
        await WriteStateAsync().ConfigureAwait(true);
        Wake();
    }

    protected new IDisposable RegisterTimer(Func<object, Task> callback, object state, TimeSpan dueTime, TimeSpan period)
        => throw new InvalidOperationException($"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns.");
}
