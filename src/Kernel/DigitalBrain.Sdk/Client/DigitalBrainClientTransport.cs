using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Webhooks;

namespace DigitalBrain.Abstractions;

internal sealed partial class DigitalBrainClientTransport
{
    private static readonly TimeSpan ResponsePollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IGrainFactory _grains;
    private readonly ActorContext? _connectionActor;

    internal DigitalBrainClientTransport(IGrainFactory grains, OwnerId owner, ActorContext? connectionActor = null)
    {
        ArgumentNullException.ThrowIfNull(grains);

        _grains = grains;
        Owner = owner;
        _connectionActor = connectionActor;
    }

    internal OwnerId Owner { get; }

    internal ActorContext? ConnectionActor => _connectionActor;

    internal PrincipalId? CurrentPrincipal
    {
        get
        {
            using var scope = EnterConnectionActor();
            return VerifiedActor.Current?.PrincipalId;
        }
    }

    internal DigitalBrainClientTransport CreateSibling(OwnerId owner, ActorContext actor)
        => new(_grains, owner, actor);

    internal NeuronId Root => IBrainNeuron.ForOwner(Owner);

    internal Task ActivateAsync(CancellationToken cancellationToken)
    {
        Scripting.ApplicationRunScope.ThrowIfDefinitionEffect("activate");
        cancellationToken.ThrowIfCancellationRequested();
        return Brain().Activate().WaitAsync(cancellationToken);
    }

    internal NeuronReference<TNeuron> GetReference<TNeuron>(
        DigitalBrainClient client,
        string name)
        where TNeuron : INeuron
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        if (!IsOwnerGlobalNeuron(typeof(TNeuron)) && (_connectionActor is not null
            || typeof(IWebhook).IsAssignableFrom(typeof(TNeuron))))
        {
            name = ScopeName(name);
        }
        return new NeuronReference<TNeuron>(client, name);
    }

    internal TEntity GetEntity<TEntity>(string name)
        where TEntity : class, IEntity
    {
        Scripting.ApplicationRunScope.ThrowIfDefinitionEffect("get entity");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainEntityContract(typeof(TEntity));
        return _grains.GetGrain<TEntity>(EntityId.For<TEntity>(Owner,
            _connectionActor is not null ? ScopeName(name) : name).ToGrainId());
    }

    private static bool IsOwnerGlobalNeuron(Type neuronType)
    {
        var type = NeuronId.GrainTypeNameOf(neuronType);
        return type.Equals("usermessages", StringComparison.OrdinalIgnoreCase)
            || type.Equals("assistant", StringComparison.OrdinalIgnoreCase);
    }

    private string ScopeName(string name)
    {
        var principal = _connectionActor?.PrincipalId ?? VerifiedActor.Current?.PrincipalId;
        if (principal is not { } actor)
        {
            return name;
        }
        if (PrincipalPartition.TryParse(name, out var owner, out _))
        {
            if (owner != actor)
            {
                throw new NeuronAuthorizationException("The requested neuron belongs to another principal.");
            }
            return name;
        }
        return PrincipalPartition.InstanceName(actor, name);
    }

    private IDisposable? EnterConnectionActor()
    {
        if (_connectionActor is null)
        {
            return null;
        }
        if (VerifiedActor.Current is { } current && current.PrincipalId != _connectionActor.PrincipalId)
        {
            throw new NeuronAuthorizationException("This connection belongs to another principal.");
        }
        return VerifiedActor.Enter(_connectionActor);
    }

    internal async Task<DeliveryOutcome> SendAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken)
        => (await SendResultAsync(receiver, signal, cancellationToken).ConfigureAwait(false)).Outcome;

    internal async Task<TResponse> SendRequestAsync<TResponse>(
        NeuronId receiver,
        Signal request,
        CancellationToken cancellationToken)
        where TResponse : Signal
    {
        var response = await SendRequestAsync(
            receiver,
            request,
            typeof(TResponse),
            cancellationToken).ConfigureAwait(false);
        return (TResponse)response;
    }

    internal Task<JournalRead> ReadJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        using var actor = EnterConnectionActor();
        RequireOwnedSubject(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();
        return Brain()
            .ReadNeuronJournal(subject, kind, afterSequence)
            .WaitAsync(cancellationToken);
    }

    internal Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        NeuronId subject,
        CancellationToken cancellationToken)
    {
        using var actor = EnterConnectionActor();
        RequireOwnedSubject(subject);
        cancellationToken.ThrowIfCancellationRequested();
        return Brain().ReadNeuronSynapses(subject).WaitAsync(cancellationToken);
    }

    internal async IAsyncEnumerable<JournalRead> WatchJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var actor = EnterConnectionActor();
        RequireOwnedSubject(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateJournalObserver(kind, out var observer, out var reference))
        {
            var cursor = afterSequence;
            while (!cancellationToken.IsCancellationRequested)
            {
                var page = await Brain()
                    .ReadNeuronJournal(subject, kind, cursor)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (page.Delta.Count > 0 || (page.UnknownEntries?.Count ?? 0) > 0 || page.ResetSnapshot is not null)
                {
                    yield return page;
                }

                cursor = page.ResumeSequence;
                await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
            }

            yield break;
        }

        var brain = Brain();
        try
        {
            await brain
                .WatchNeuron(subject, kind, afterSequence, reference)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            await foreach (var page in observer.Reads
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return page;
            }
        }
        finally
        {
            await TeardownWatchAsync(brain, subject, reference, observer).ConfigureAwait(false);
        }
    }

    private IBrainNeuron Brain()
        => _grains.GetGrain<IBrainNeuron>(Root.ToGrainId());

    private async Task<SignalDeliveryResult> SendResultAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken)
    {
        Scripting.ApplicationRunScope.ThrowIfDefinitionEffect("send");
        using var actor = EnterConnectionActor();
        RequireOwnedSubject(receiver);
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        return await Brain().Send(receiver, signal, cancellationToken)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Signal> SendRequestAsync(
        NeuronId receiver,
        Signal request,
        Type responseType,
        CancellationToken cancellationToken)
    {
        Scripting.ApplicationRunScope.ThrowIfDefinitionEffect("request");
        using var actor = EnterConnectionActor();
        using var budget = SignalRequestPolicy.CreateBudget(cancellationToken);
        try
        {
            return await SendRequestCoreAsync(receiver, request, responseType, budget.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw SignalRequestPolicy.TimedOut(receiver, request, exception);
        }
    }

    private async Task<Signal> SendRequestCoreAsync(
        NeuronId receiver,
        Signal request,
        Type responseType,
        CancellationToken cancellationToken)
    {
        RequireOwnedSubject(receiver);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseType);
        if (!typeof(Signal).IsAssignableFrom(responseType)
            || responseType.IsAbstract
            || responseType.IsInterface)
        {
            throw new ArgumentException(
                $"Response type '{responseType}' must be a concrete Signal.",
                nameof(responseType));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var brain = Brain();
        var cursor = await brain
            .ReadNeuronJournal(Root, JournalKind.Incoming, afterSequence: long.MaxValue)
            .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken)
            .ConfigureAwait(false);

        if (!TryCreateJournalObserver(JournalKind.Incoming, out var observer, out var reference))
        {
            var result = await SendResultAsync(receiver, request, cancellationToken).ConfigureAwait(false);
            SignalRequestPolicy.RequireHandled(receiver, request, result.Outcome);
            return await PollForResponseAsync(
                brain,
                Root,
                cursor.ResumeSequence,
                receiver,
                result.Delivery,
                responseType,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await brain
                .WatchNeuron(Root, JournalKind.Incoming, cursor.ResumeSequence, reference)
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken)
                .ConfigureAwait(false);

            var result = await SendResultAsync(receiver, request, cancellationToken).ConfigureAwait(false);
            SignalRequestPolicy.RequireHandled(receiver, request, result.Outcome);
            return await WaitForResponseAsync(
                observer,
                brain,
                Root,
                receiver,
                result.Delivery,
                responseType,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await TeardownWatchAsync(brain, Root, reference, observer).ConfigureAwait(false);
        }
    }

    private void RequireOwnedSubject(NeuronId subject)
    {
        if (subject.Owner != Owner)
        {
            throw new NeuronAuthorizationException(
                $"Client owner '{Owner}' cannot access neuron '{subject}' owned by '{subject.Owner}'.");
        }
    }

    private bool TryCreateJournalObserver(
        JournalKind kind,
        [NotNullWhen(true)] out ChannelJournalObserver? observer,
        [NotNullWhen(true)] out IJournalObserver? reference)
    {
        var candidate = new ChannelJournalObserver(kind);
        try
        {
            reference = _grains.CreateObjectReference<IJournalObserver>(candidate);
            observer = candidate;
            return true;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("object reference", StringComparison.OrdinalIgnoreCase))
        {
            candidate.Complete();
            observer = null;
            reference = null;
            return false;
        }
    }

    private static async Task<Signal> WaitForResponseAsync(
        ChannelJournalObserver observer,
        IBrainNeuron brain,
        NeuronId root,
        NeuronId receiver,
        SignalDelivery request,
        Type responseType,
        CancellationToken cancellationToken)
    {
        await foreach (var page in observer.Reads.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var retained = await SignalRequestPolicy.RecoverRetainedAsync(page,
                after => brain.ReadNeuronJournal(root, JournalKind.Incoming, after)
                    .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken)).ConfigureAwait(false);
            if (SignalRequestPolicy.FindResponse(retained, receiver, request, responseType) is { } response)
            {
                return response;
            }
        }

        throw new InvalidOperationException(
            $"The root journal watch ended before a '{responseType.Name}' response arrived for request '{request.SignalId}'.");
    }

    private static async Task<Signal> PollForResponseAsync(
        IBrainNeuron brain,
        NeuronId root,
        long cursor,
        NeuronId receiver,
        SignalDelivery request,
        Type responseType,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var read = await brain
                .ReadNeuronJournal(root, JournalKind.Incoming, cursor)
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken)
                .ConfigureAwait(false);
            var retained = await SignalRequestPolicy.RecoverRetainedAsync(read,
                after => brain.ReadNeuronJournal(root, JournalKind.Incoming, after)
                    .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken)).ConfigureAwait(false);
            if (SignalRequestPolicy.FindResponse(retained, receiver, request, responseType) is { } response)
            {
                return response;
            }

            cursor = retained.ResumeSequence;
            await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TeardownWatchAsync(
        IBrainNeuron brain,
        NeuronId subject,
        IJournalObserver reference,
        ChannelJournalObserver observer)
    {
        try
        {
            await brain.UnwatchNeuron(subject, reference)
                .WaitAsync(NeuronCallTimeouts.LookupBound).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            _grains.DeleteObjectReference<IJournalObserver>(reference);
        }
        catch (Exception)
        {
        }
        finally
        {
            observer.Complete();
        }
    }

    private static void RequireDomainNeuronContract(Type neuronType)
    {
        if (neuronType == typeof(INeuron)
            || typeof(IBrainNeuron).IsAssignableFrom(neuronType))
        {
            throw new NeuronAuthorizationException(
                $"'{neuronType.Name}' is not addressable through IDigitalBrain.Get. Activate the brain with ActivateAsync; address domain neuron contracts with Get; send signals through NeuronReference.SendAsync; observe the owner root through IDigitalBrain and domain neurons through NeuronReference.");
        }
    }

    private static void RequireDomainEntityContract(Type entityType)
    {
        if (entityType == typeof(IEntity))
        {
            throw new NeuronAuthorizationException(
                $"'{entityType.Name}' is not addressable through IDigitalBrain.GetEntity. Address a concrete entity contract with GetEntity.");
        }
    }
}
