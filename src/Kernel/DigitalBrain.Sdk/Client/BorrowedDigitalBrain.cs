using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

internal sealed class BorrowedDigitalBrain(IDigitalBrain inner) : IDigitalBrain
{
    internal IDigitalBrain Inner { get; } = inner;

    public OwnerId Owner => Inner.Owner;
    public BrainRoot Root => Inner.Root;
    public Task ActivateAsync(CancellationToken cancellationToken = default) => Inner.ActivateAsync(cancellationToken);
    public NeuronReference<TNeuron> Get<TNeuron>(string name = "default") where TNeuron : INeuron
        => Inner.Get<TNeuron>(name);
    public TEntity GetEntity<TEntity>(string name = "default") where TEntity : class, IEntity
        => Inner.GetEntity<TEntity>(name);
    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
        => Inner.ReadJournalAsync(kind, afterSequence, cancellationToken);
    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
        => Inner.WatchJournalAsync(kind, afterSequence, cancellationToken);
    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(CancellationToken cancellationToken = default)
        => Inner.GetSynapsesAsync(cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
