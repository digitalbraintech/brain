using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Core;

internal sealed class SignalSender
{
    private readonly NeuronId _source;
    private readonly TimeProvider _clock;
    private readonly SignalRouter _router;
    private readonly NeuronJournals _journals;
    private readonly NeuronSynapses _synapses;
    private readonly IGrainFactory _grains;
    private readonly Func<SignalDelivery, CancellationToken, Task<DeliveryOutcome>> _deliverLocally;
    private readonly Func<CancellationToken, ValueTask> _persist;
    private readonly ActivityReporter _activities;

    internal SignalSender(
        NeuronId source,
        TimeProvider clock,
        SignalRouter router,
        NeuronJournals journals,
        NeuronSynapses synapses,
        IGrainFactory grains,
        Func<SignalDelivery, CancellationToken, Task<DeliveryOutcome>> deliverLocally,
        Func<CancellationToken, ValueTask> persist)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(journals);
        ArgumentNullException.ThrowIfNull(synapses);
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(deliverLocally);
        ArgumentNullException.ThrowIfNull(persist);

        _source = source;
        _clock = clock;
        _router = router;
        _journals = journals;
        _synapses = synapses;
        _grains = grains;
        _deliverLocally = deliverLocally;
        _persist = persist;
        _activities = new ActivityReporter(source, grains, clock);
    }

    internal Task<SignalDeliveryResult> SendAsync(
        NeuronId receiver,
        Signal signal,
        SignalDelivery? cause,
        CancellationToken cancellationToken = default)
        => SendAsync(receiver, signal, cause, correlation: null, cancellationToken);

    internal async Task<SignalDeliveryResult> SendAsync(
        NeuronId receiver,
        Signal signal,
        SignalDelivery? cause,
        CorrelationId? correlation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        using var path = NeuronRequestPath.Enter(_source, receiver);

        var delivery = await RecordOutgoingAsync(signal, cause, correlation)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReportDispatchAsync(delivery, "running", receiver).ConfigureAwait(true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Bound the remote await here, inside the state-owning sender. A timeout around
            // SendAsync itself would leave this continuation alive to reinforce a route
            // after the caller's serialized turn had already unwound.
            var handling = DeliverAsync(receiver, delivery, DeliveryMode.Awaited, cancellationToken);
            // Local self-send shares our mutable activation state, so it must unwind
            // cooperatively before this turn can end. Only remote work can be detached.
            var outcome = await (receiver == _source ? handling : handling.WaitAsync(cancellationToken))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            cancellationToken.ThrowIfCancellationRequested();

            if (outcome == DeliveryOutcome.Handled)
            {
                _synapses.Reinforce(receiver, signal.GetType().Name, SynapseKind.Learned);
                await _persist(CancellationToken.None)
                    .ConfigureAwait(true);
            }

            await ReportDispatchAsync(delivery, "completed", receiver).ConfigureAwait(true);
            return new SignalDeliveryResult(delivery, outcome);
        }
        catch (Exception exception)
        {
            await ReportDispatchAsync(delivery, exception is OperationCanceledException ? "cancelled" : "failed",
                receiver, exception.Message).ConfigureAwait(true);
            throw;
        }
    }

    internal Task<int> BroadcastAsync(Signal signal, SignalDelivery? cause)
        => BroadcastAsync(signal, cause, correlation: null);

    internal async Task<int> BroadcastAsync(
        Signal signal,
        SignalDelivery? cause,
        CorrelationId? correlation)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // One owner-visible fact: empty audience still journals so SSE and the
        // debugger see the event. Fan-out reuses that envelope; it does not
        // record N outgoing copies.
        var delivery = await RecordOutgoingAsync(signal, cause, correlation)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        // Keep one dispatch operation open across the complete audience, including
        // subscribers which have not started yet. An early recipient cannot settle it.
        await ReportDispatchAsync(delivery, "running", target: null).ConfigureAwait(true);
        try
        {

            var receivers = _router.Matching(signal, _source, _synapses, delivery.CorrelationId);
            if (receivers.Count == 0)
            {
                await ReportDispatchAsync(delivery, "completed", target: null).ConfigureAwait(true);
                return 0;
            }

            var signalType = signal.GetType().Name;
            List<Exception>? failures = null;
            var learned = false;
            foreach (var synapse in receivers)
            {
                var receiver = synapse.Target;
                try
                {
                    using var path = NeuronRequestPath.Enter(_source, receiver);
                    var handling = DeliverAsync(receiver, delivery, DeliveryMode.Awaited, CancellationToken.None);
                    var outcome = await (receiver == _source ? handling : handling.WaitAsync(CancellationToken.None))
                        .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                    if (outcome == DeliveryOutcome.Handled)
                    {
                        _synapses.Reinforce(receiver, signalType, SynapseKind.Learned, synapse.Correlation);
                        learned = true;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // A broken subscriber must not prevent delivery to the remaining audience.
                    (failures ??= []).Add(error);
                }
            }

            if (learned)
            {
                await _persist(CancellationToken.None).ConfigureAwait(true);
            }

            if (failures is not null)
            {
                throw new AggregateException("One or more signal subscribers failed to handle the broadcast.", failures);
            }

            await ReportDispatchAsync(delivery, "completed", target: null).ConfigureAwait(true);
            return receivers.Count;
        }
        catch (Exception exception)
        {
            await ReportDispatchAsync(delivery, exception is OperationCanceledException ? "cancelled" : "failed",
                target: null, exception.Message).ConfigureAwait(true);
            throw;
        }
    }

    internal async Task ReplyAsync(Signal response, SignalDelivery handling)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(handling);

        var delivery = await RecordOutgoingAsync(response, handling, handling.CorrelationId)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReportDispatchAsync(delivery, "running", handling.Caller).ConfigureAwait(true);

        using (NeuronRequestPath.Clear())
        {
            _ = ObserveDetachedAsync(handling.Caller, delivery);
        }
    }

    internal async Task<SignalDelivery> RecordOutgoingAsync(
        Signal signal,
        SignalDelivery? cause,
        CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var delivery = SignalDelivery.Create(
            signal,
            _source,
            _journals.OutgoingNextSequence,
            _clock,
            cause,
            correlation,
            principal: VerifiedActor.Current?.PrincipalId ?? cause?.Principal);

        return await RecordOutgoingAsync(delivery).ConfigureAwait(true);
    }

    internal async Task<SignalDelivery> RecordOutgoingAsync(SignalDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.Caller != _source)
        {
            throw new NeuronAuthorizationException("An outgoing envelope must belong to its source neuron.");
        }
        _journals.AppendOutgoing(delivery);
        await _persist(CancellationToken.None)
            .ConfigureAwait(true);
        await _journals.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await _activities.ReportAsync(delivery, $"{_source}/{delivery.SignalId}/record", "observed", target: null)
            .ConfigureAwait(true);

        return delivery;
    }

    private Task<DeliveryOutcome> DeliverAsync(
        NeuronId receiver,
        SignalDelivery delivery,
        DeliveryMode mode,
        CancellationToken cancellationToken = default)
        // Same-activation Deliver is in-process: a serialized neuron cannot await its own
        // grain proxy. Incoming/outgoing call filters therefore do not see this path;
        // journal and synapse population stay here, not in a filter.
        => mode == DeliveryMode.Awaited && receiver == _source
            ? _deliverLocally(delivery, cancellationToken)
            : _grains.GetGrain<INeuronGrain>(receiver.ToGrainId()).Deliver(delivery, cancellationToken);

    private async Task ObserveDetachedAsync(NeuronId receiver, SignalDelivery delivery)
    {
        try
        {
            _ = await DeliverAsync(receiver, delivery, DeliveryMode.Detached)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await ReportDispatchAsync(delivery, "completed", receiver).ConfigureAwait(true);
        }
        catch (Exception undelivered)
        {
            SignalTelemetry.ReplyDropped(_source, receiver, undelivered);
            await ReportDispatchAsync(delivery, undelivered is OperationCanceledException ? "cancelled" : "failed",
                receiver, undelivered.Message).ConfigureAwait(true);
        }
    }

    private Task ReportDispatchAsync(SignalDelivery delivery, string phase, NeuronId? target, string? detail = null)
        => _activities.ReportAsync(delivery, $"{_source}/{delivery.SignalId}/dispatch", phase, target, detail);

    private enum DeliveryMode : byte
    {
        Awaited,
        Detached,
    }
}
