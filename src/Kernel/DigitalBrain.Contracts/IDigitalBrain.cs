using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions;

// The owner's typed handle on the graph. Address neurons with Get, entities with GetEntity.
// Deliver with SendAsync (only compiles when the target IHandle<T>s the signal).
// SubscribeTo writes a durable Bound synapse on the source.
public interface IDigitalBrain : IAsyncDisposable
{
    OwnerId Owner { get; }

    Task ActivateAsync(CancellationToken cancellationToken = default);

    TSignal Input<TSignal>() where TSignal : Signal
        => throw new InvalidOperationException("Input is available only while a saved behavior is executing.");

    NeuronId InputSource
        => throw new InvalidOperationException("InputSource is available only while a saved behavior is executing.");

    NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron;

    TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity;

    Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default);
}
