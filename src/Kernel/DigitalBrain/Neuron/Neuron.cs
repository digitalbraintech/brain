using System.Diagnostics;
using DigitalBrain.Abstractions;
using Orleans.Journaling;
using Microsoft.Extensions.DependencyInjection;

using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Synapses;
namespace DigitalBrain.Core;

public abstract class Neuron :
    DurableGrain,
    INeuron,
    INeuronGrain,
    INeuronQuery
{
    private readonly NeuronActivationComponents _components;
    private readonly SignalSender _sender;
    private SignalDelivery? _handling;
    private readonly IDurableDictionary<string, long> _sourceEpochs;

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _components = runtime.Bind(ServiceProvider, Id);
        _sourceEpochs = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, long>>("neuron.source-epochs");
        _sender = new SignalSender(
            Id,
            _components.Clock,
            _components.Router,
            _components.Journals,
            _components.Synapses,
            GrainFactory,
            DispatchDeliveryAsync,
            WriteStateAsync);
    }

    public NeuronId Id
        => NeuronId.FromGrainKey(
            this.GetGrainId().Type.ToString()!,
            this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider => _components.Clock;

    protected SignalDelivery? CurrentDelivery => _handling;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());

        await base.OnActivateAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await OnNeuronActivatedAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<DeliveryOutcome> Deliver(
        SignalDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        return await DispatchDeliveryAsync(delivery, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_components.Journals.Read(kind, afterSequence));

    public Task<IReadOnlyList<Synapse>> ReadSynapses()
        => Task.FromResult(_components.Synapses.Active());

    public async Task Watch(
        JournalKind kind,
        long afterSequence,
        IJournalObserver observer)
        => await _components.Journals.WatchAsync(kind, afterSequence, observer)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public Task Unwatch(IJournalObserver observer)
    {
        _components.Journals.Unwatch(observer);
        return Task.CompletedTask;
    }

    protected Task<SignalDeliveryResult> SendAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken = default)
        => _sender.SendAsync(receiver, signal, _handling, cancellationToken);

    /// <summary>
    /// Sends from this activation and reads the exact reply from the target's outgoing
    /// journal. It does not re-enter the owner root or wait for a reply to enter this
    /// busy serialized activation.
    /// </summary>
    protected async Task<TResponse> RequestAsync<TResponse>(
        NeuronId receiver,
        Signal<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Signal
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSameOwner(receiver);
        if (receiver == Id)
        {
            throw new InvalidOperationException("A neuron cannot request a reply from itself.");
        }

        using var budget = SignalRequestPolicy.CreateBudget(cancellationToken);
        try
        {
            budget.Token.ThrowIfCancellationRequested();
            var target = GrainFactory.GetGrain<INeuronQuery>(receiver.ToGrainId());
            var cursor = await target.ReadJournal(JournalKind.Outgoing, long.MaxValue)
                .WaitAsync(NeuronCallTimeouts.LookupBound, budget.Token)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            var result = await SendAsync(receiver, request, budget.Token)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            SignalRequestPolicy.RequireHandled(receiver, request, result.Outcome);

            // Replies are observed at the target. Waiting on this busy caller's
            // incoming journal would deadlock its serialized turn.
            var afterSequence = cursor.ResumeSequence;
            while (true)
            {
                var replies = await target.ReadJournal(JournalKind.Outgoing, afterSequence)
                    .WaitAsync(NeuronCallTimeouts.LookupBound, budget.Token).ConfigureAwait(true);
                var retained = await SignalRequestPolicy.RecoverRetainedAsync(replies,
                    after => target.ReadJournal(JournalKind.Outgoing, after)
                        .WaitAsync(NeuronCallTimeouts.LookupBound, budget.Token)).ConfigureAwait(true);
                if (SignalRequestPolicy.FindResponse(retained, receiver, result.Delivery, typeof(TResponse)) is TResponse response)
                {
                    return response;
                }
                if (request is not IDeferredReply)
                {
                    throw SignalRequestPolicy.MissingResponse(receiver, result.Delivery, typeof(TResponse), replies.ResetSnapshot is not null);
                }
                afterSequence = retained.ResumeSequence;
                await Task.Delay(100, budget.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException exception) when (
            budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw SignalRequestPolicy.TimedOut(receiver, request, exception);
        }
    }

    protected Task<int> BroadcastAsync(Signal signal)
        => _sender.BroadcastAsync(signal, _handling);

    protected IReadOnlyList<NeuronId> BroadcastRecipients(Signal signal)
        => _components.Router.BroadcastRecipientsFor(signal, Id, _components.Synapses);

    // Durable off-turn executors acknowledge direct requests here after handling.
    // The caller commits this mutation with its operation acknowledgement.
    protected void ReinforceDelivery(NeuronId receiver, string signalType)
        => _components.Synapses.Reinforce(receiver, signalType, SynapseKind.Learned);

    protected Task SubscribeToAsync<TSource, TSignal>(NeuronId source)
        where TSource : INeuron
        where TSignal : Signal
        => BindFromAsync(source, typeof(TSignal).Name, typeof(TSource));

    protected Task UnsubscribeFromAsync<TSource, TSignal>(NeuronId source)
        where TSource : INeuron
        where TSignal : Signal
        => UnbindFromAsync(source, typeof(TSignal).Name, typeof(TSource));

    protected Task ReplyAsync(Signal response)
        => _sender.ReplyAsync(
            response,
            _handling
                ?? throw new InvalidOperationException(
                    "ReplyAsync requires an active delivery context. Reply only from a HandleAsync turn."));

    protected Task<SignalDelivery> RecordOutgoingAsync(Signal signal)
        => _sender.RecordOutgoingAsync(signal, _handling);

    // An outbox persists this envelope with its work before attempting delivery.
    protected SignalDelivery CreateDelivery(
        Signal signal, SignalDelivery? cause = null, SignalId? signalId = null, long? sourceEpoch = null,
        string? sourceStream = null, long? streamGeneration = null)
        => SignalDelivery.Create(signal, Id, _components.Journals.OutgoingNextSequence,
            TimeProvider, cause ?? _handling,
            principal: VerifiedActor.Current?.PrincipalId ?? cause?.Principal ?? _handling?.Principal,
            signalId: signalId, sourceEpoch: sourceEpoch,
            sourceStream: sourceStream, streamGeneration: streamGeneration);

    protected Task<SignalDelivery> RecordOutgoingAsync(SignalDelivery delivery)
        => _sender.RecordOutgoingAsync(delivery);

    protected Task ReplyToAsync(SignalDelivery request, Signal response)
        => _sender.ReplyAsync(response, request);

    protected virtual Task<DeliveryOutcome> HandleUnmatchedAsync(
        SignalDelivery delivery, CancellationToken cancellationToken)
        => Task.FromResult(DeliveryOutcome.Unhandled);

    protected virtual Task OnSubscriptionChangedAsync(
        NeuronId source, string signalType, bool subscribed)
        => Task.CompletedTask;

    protected virtual Task ValidateSubscriptionAsync(
        NeuronId source, string signalType, bool subscribed)
        => Task.CompletedTask;

    protected new IDisposable RegisterTimer(
        Func<object, Task> callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period)
        => throw new InvalidOperationException(
            $"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require "
            + "serialized turns.");

    private async Task<DeliveryOutcome> DispatchDeliveryAsync(
        SignalDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (!IsDeliveryCurrent(delivery))
        {
            return DeliveryOutcome.Handled;
        }
        using var handling = SignalTelemetry.Source.StartActivity("handle");

        handling?.SetTag(SignalTelemetry.ReceiverTag, Id.ToString());
        handling?.SetTag(SignalTelemetry.SignalTag, delivery.Signal.GetType().Name);
        handling?.SetTag(SignalTelemetry.CorrelationTag, delivery.CorrelationId.ToString());

        var previousHandling = _handling;
        _handling = delivery;

        // Re-enter the verified principal that rode the delivery so grants, graph
        // partition, and stamps apply on the receiving turn.
        using var principalScope = VerifiedActor.Enter(
            delivery.Principal is { } principal
                ? new ActorContext(principal, "_delivery")
                : null);

        try
        {
            var outcome = await _components.Dispatcher.DispatchAsync(
                    this,
                    delivery.Signal,
                    cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (outcome == DeliveryOutcome.Unhandled)
            {
                outcome = await HandleUnmatchedAsync(delivery, cancellationToken).ConfigureAwait(true);
            }

            _components.Journals.AppendIncoming(delivery);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            await _components.Journals.NotifyWatchersAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return outcome;
        }
        catch (Exception failure)
        {
            handling?.SetStatus(ActivityStatusCode.Error, failure.Message);
            throw;
        }
        finally
        {
            _handling = previousHandling;
        }
    }

    public Task HandleAsync(Subscribe signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return BindFromAsync(signal.Source, signal.SignalType, expectedSourceType: null);
    }

    public Task HandleAsync(Unsubscribe signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return UnbindFromAsync(signal.Source, signal.SignalType, expectedSourceType: null);
    }

    public async Task BindOutgoing(NeuronId subscriber, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        RequireSameOwner(subscriber);
        _components.Synapses.Bind(subscriber, signalType);
        await WriteStateAsync().ConfigureAwait(true);
    }

    public async Task UnbindOutgoing(NeuronId subscriber, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        RequireSameOwner(subscriber);
        _components.Synapses.Unbind(subscriber, signalType);
        await WriteStateAsync().ConfigureAwait(true);
    }

    public async Task FenceSourceEpoch(NeuronId source, long minimumEpoch)
    {
        RequireSameOwner(source);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumEpoch);
        var key = source.ToString();
        if (!_sourceEpochs.TryGetValue(key, out var current) || current < minimumEpoch)
        {
            _sourceEpochs[key] = minimumEpoch;
        }
        await WriteStateAsync().ConfigureAwait(true);
        await OnSourceEpochFencedAsync(source, Math.Max(current, minimumEpoch)).ConfigureAwait(true);
    }

    protected bool IsDeliveryCurrent(SignalDelivery delivery)
        => !(delivery.SourceEpoch is { } epoch
                && _sourceEpochs.TryGetValue(delivery.Caller.ToString(), out var minimum) && epoch < minimum)
            && !(delivery.SourceStream is { } stream && delivery.StreamGeneration is { } generation
                && _sourceEpochs.TryGetValue(StreamFenceKey(delivery.Caller, stream), out var minimumGeneration)
                && generation < minimumGeneration);

    protected virtual Task OnSourceEpochFencedAsync(NeuronId source, long minimumEpoch)
        => Task.CompletedTask;

    protected virtual Task OnSourceStreamFencedAsync(NeuronId source, string stream, long minimumGeneration)
        => Task.CompletedTask;

    public async Task FenceSourceStream(NeuronId source, string stream, long minimumGeneration)
    {
        RequireSameOwner(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumGeneration);
        var key = StreamFenceKey(source, stream);
        if (!_sourceEpochs.TryGetValue(key, out var current) || current < minimumGeneration)
        {
            _sourceEpochs[key] = minimumGeneration;
        }
        await WriteStateAsync().ConfigureAwait(true);
        await OnSourceStreamFencedAsync(source, stream, Math.Max(current, minimumGeneration)).ConfigureAwait(true);
    }

    private static string StreamFenceKey(NeuronId source, string stream)
        => $"{source}|{stream.Length}:{stream}";

    private async Task BindFromAsync(NeuronId source, string signalType, Type? expectedSourceType)
    {
        RequireSubscription(source, signalType, expectedSourceType);
        await ValidateSubscriptionAsync(source, signalType, subscribed: true).ConfigureAwait(true);
        using var path = NeuronRequestPath.Enter(Id, source);
        var binding = source == Id
            ? BindOutgoing(Id, signalType)
            : GrainFactory.GetGrain<INeuronGrain>(source.ToGrainId()).BindOutgoing(Id, signalType)
                .WaitAsync(NeuronCallTimeouts.LookupBound);
        await binding
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await OnSubscriptionChangedAsync(source, signalType, subscribed: true).ConfigureAwait(true);
    }

    private async Task UnbindFromAsync(NeuronId source, string signalType, Type? expectedSourceType)
    {
        RequireSubscription(source, signalType, expectedSourceType);
        await ValidateSubscriptionAsync(source, signalType, subscribed: false).ConfigureAwait(true);
        using var path = NeuronRequestPath.Enter(Id, source);
        var binding = source == Id
            ? UnbindOutgoing(Id, signalType)
            : GrainFactory.GetGrain<INeuronGrain>(source.ToGrainId()).UnbindOutgoing(Id, signalType)
                .WaitAsync(NeuronCallTimeouts.LookupBound);
        await binding
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await OnSubscriptionChangedAsync(source, signalType, subscribed: false).ConfigureAwait(true);
    }

    private void RequireSubscription(NeuronId source, string signalType, Type? expectedSourceType)
    {
        if (string.IsNullOrWhiteSpace(signalType))
        {
            throw new NeuronAuthorizationException($"Neuron '{Id}' refuses a subscription without a signal type.");
        }

        RequireSameOwner(source);
        if (expectedSourceType is not null
            && source.Type != NeuronId.GrainTypeNameOf(expectedSourceType).ToLowerInvariant())
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{source}' is not a '{expectedSourceType.Name}' instance.");
        }

        if (!CanHandle(signalType))
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{Id}' cannot subscribe to '{signalType}' because it does not IHandle it.");
        }
    }

    private void RequireSameOwner(NeuronId other)
    {
        if (other.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{Id}' refuses a foreign owner '{other.Owner}'.");
        }
    }

    protected virtual bool CanHandle(string signalType)
        => GetType().GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IHandle<>)
            && string.Equals(contract.GetGenericArguments()[0].Name, signalType, StringComparison.Ordinal));
}
