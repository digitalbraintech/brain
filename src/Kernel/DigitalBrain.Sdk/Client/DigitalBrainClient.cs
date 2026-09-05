using System.ComponentModel;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

public sealed partial class DigitalBrainClient : IDigitalBrain, INeuronClient
{
    private readonly DigitalBrainClientTransport _transport;

    private DigitalBrainClient(DigitalBrainClientTransport transport)
        => _transport = transport;

    public OwnerId Owner => _transport.Owner;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DigitalBrainClient Connect(IGrainFactory grains, string owner)
        => new(new DigitalBrainClientTransport(grains, new OwnerId(owner)));

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DigitalBrainClient Connect(IGrainFactory grains, string owner, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new(new DigitalBrainClientTransport(grains, new OwnerId(owner), actor));
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default)
        => _transport.ActivateAsync(cancellationToken);

    public NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron
        => _transport.GetReference<TNeuron>(this, name);

    public TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity
        => _transport.GetEntity<TEntity>(name);

    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _transport.ReadJournalAsync(_transport.Root, kind, afterSequence, cancellationToken);

    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _transport.WatchJournalAsync(_transport.Root, kind, afterSequence, cancellationToken);

    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default)
        => _transport.GetSynapsesAsync(_transport.Root, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _host, null) is not { } host)
        {
            return;
        }
        try { await host.StopAsync().ConfigureAwait(false); }
        finally { host.Dispose(); }
    }

    Task<DeliveryOutcome> INeuronClient.SendAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken)
        => _transport.SendAsync(receiver, signal, cancellationToken);

    Task<TResponse> INeuronClient.SendRequestAsync<TResponse>(
        NeuronId receiver,
        Signal request,
        CancellationToken cancellationToken)
        => _transport.SendRequestAsync<TResponse>(receiver, request, cancellationToken);

    Task<JournalRead> INeuronClient.ReadJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
        => _transport.ReadJournalAsync(subject, kind, afterSequence, cancellationToken);

    IAsyncEnumerable<JournalRead> INeuronClient.WatchJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
        => _transport.WatchJournalAsync(subject, kind, afterSequence, cancellationToken);

    Task<IReadOnlyList<Synapse>> INeuronClient.GetSynapsesAsync(
        NeuronId subject,
        CancellationToken cancellationToken)
        => _transport.GetSynapsesAsync(subject, cancellationToken);
}
