using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Scripting.Tests;

internal sealed class FakeDigitalBrain(string owner) : IDigitalBrain
{
    public OwnerId Owner { get; } = new(owner);

    public JournalRead InitialJournal { get; set; } = new(0, [], null);

    public Func<JournalKind, long, CancellationToken, IAsyncEnumerable<JournalRead>> WatchJournal { get; set; }
        = static (_, _, _) => EmptyJournal();

    public bool WasWatchJournalCalled { get; private set; }

    public int ActivateCallCount { get; private set; }

    public long? WatchJournalAfterSequence { get; private set; }

    public JournalKind? ReadJournalKind { get; private set; }

    public long? ReadJournalAfterSequence { get; private set; }

    public CancellationToken ReadJournalCancellationToken { get; private set; }

    public CancellationToken WatchJournalCancellationToken { get; private set; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        ActivateCallCount++;
        return Task.CompletedTask;
    }

    public Task<int> PublishAsync(Signal signal, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron => throw new NotSupportedException();

    public TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity => throw new NotSupportedException();

    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        ReadJournalKind = kind;
        ReadJournalAfterSequence = afterSequence;
        ReadJournalCancellationToken = cancellationToken;
        return Task.FromResult(InitialJournal);
    }

    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        WasWatchJournalCalled = true;
        WatchJournalAfterSequence = afterSequence;
        WatchJournalCancellationToken = cancellationToken;
        return WatchJournal(kind, afterSequence, cancellationToken);
    }

    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    private static async IAsyncEnumerable<JournalRead> EmptyJournal()
    {
        yield break;
    }
}
