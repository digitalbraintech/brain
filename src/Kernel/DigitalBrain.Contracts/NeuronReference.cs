using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

public readonly struct NeuronReference<TNeuron> : IEquatable<NeuronReference<TNeuron>>, INeuronReference
    where TNeuron : INeuron
{
    private readonly INeuronClient _client;
    private readonly string _name;

    internal NeuronReference(INeuronClient client, string name)
    {
        _client = client;
        _name = name;
    }

    public NeuronId Id => NeuronId.For<TNeuron>(_client.Owner, _name);

    public async Task<IReadOnlyList<string>> PublishedSignalTypesAsync(CancellationToken cancellationToken = default)
    {
        if (typeof(TNeuron) == typeof(IBehavior))
        {
            var read = await _client.SendRequestAsync<BehaviorRead>(Id, new ReadBehavior(), cancellationToken).ConfigureAwait(false);
            return (read.Behavior.Draft ?? read.Behavior.Active)?.OutputSignalTypes ?? [];
        }
        return typeof(TNeuron).GetInterfaces().Where(t => t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(IEmits<>))
            .Select(t => t.GetGenericArguments()[0].Name).Distinct(StringComparer.Ordinal).ToArray();
    }

    public Task SubscribeAsync<TSignal>(INeuronReference source, CancellationToken cancellationToken = default)
        where TSignal : Signal
        => SubscribeCoreAsync(source, typeof(TSignal).Name, cancellationToken);

    public async Task SubscribeAsync(INeuronReference source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<string> accepted;
        if (typeof(TNeuron) == typeof(IBehavior))
        {
            var read = await _client.SendRequestAsync<BehaviorRead>(Id, new ReadBehavior(), cancellationToken).ConfigureAwait(false);
            accepted = (read.Behavior.Draft ?? read.Behavior.Active)?.InputSignalTypes ?? [];
        }
        else
        {
            accepted = typeof(TNeuron).GetInterfaces().Where(t => t.IsGenericType
                    && t.GetGenericTypeDefinition() == typeof(IHandle<>))
                .Select(t => t.GetGenericArguments()[0].Name).ToArray();
        }
        var published = await source.PublishedSignalTypesAsync(cancellationToken).ConfigureAwait(false);
        var compatible = accepted.Intersect(published, StringComparer.Ordinal).ToArray();
        if (compatible.Length != 1)
        {
            throw new InvalidOperationException("A subscription needs exactly one compatible signal type. Use SubscribeAsync<TSignal>(source) to choose explicitly.");
        }
        await SubscribeCoreAsync(source, compatible[0], cancellationToken).ConfigureAwait(false);
    }

    public async Task UnsubscribeAsync<TSignal>(INeuronReference source, CancellationToken cancellationToken = default)
        where TSignal : Signal
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = await DeliverAsync(new Unsubscribe(source.Id, typeof(TSignal).Name), cancellationToken).ConfigureAwait(false);
        if (result != DeliveryOutcome.Handled)
        {
            throw new InvalidOperationException($"Unsubscription was {result}.");
        }
    }

    private async Task SubscribeCoreAsync(INeuronReference source, string signalType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = await DeliverAsync(new Subscribe(source.Id, signalType), cancellationToken).ConfigureAwait(false);
        if (result != DeliveryOutcome.Handled)
        {
            throw new InvalidOperationException($"Subscription was {result}.");
        }
    }

    internal Task<TResponse> RequestCoreAsync<TResponse>(
        Signal<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Signal
        => _client.SendRequestAsync<TResponse>(Id, request, cancellationToken);

    internal Task<DeliveryOutcome> DeliverAsync(
        Signal signal,
        CancellationToken cancellationToken)
        => _client.SendAsync(Id, signal, cancellationToken);

    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _client.ReadJournalAsync(Id, kind, afterSequence, cancellationToken);

    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _client.WatchJournalAsync(Id, kind, afterSequence, cancellationToken);

    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default)
        => _client.GetSynapsesAsync(Id, cancellationToken);

    public bool Equals(NeuronReference<TNeuron> other)
        => ReferenceEquals(_client, other._client)
            && string.Equals(_name, other._name, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is NeuronReference<TNeuron> other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(_client, _name);

    public static bool operator ==(NeuronReference<TNeuron> left, NeuronReference<TNeuron> right)
        => left.Equals(right);

    public static bool operator !=(NeuronReference<TNeuron> left, NeuronReference<TNeuron> right)
        => !left.Equals(right);
}
