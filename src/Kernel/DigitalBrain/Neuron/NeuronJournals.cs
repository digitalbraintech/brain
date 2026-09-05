using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Core;

internal sealed class NeuronJournals
{
    private readonly NeuronId _neuronId;
    private readonly JournalWindow _incoming;
    private readonly JournalWindow _outgoing;
    private readonly List<Watcher> _watchers = [];

    internal NeuronJournals(NeuronId neuronId, JournalWindow incoming, JournalWindow outgoing)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(outgoing);

        _neuronId = neuronId;
        _incoming = incoming;
        _outgoing = outgoing;
    }

    internal long OutgoingNextSequence => _outgoing.NextSequence;

    internal JournalRead Read(JournalKind kind, long afterSequence)
        => WindowFor(kind).Read(afterSequence);

    internal async Task WatchAsync(
        JournalKind kind,
        long afterSequence,
        IJournalObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        _ = WindowFor(kind);
        _watchers.RemoveAll(existing =>
            existing.Kind == kind && existing.Observer.Equals(observer));

        var watcher = new Watcher(observer, kind, afterSequence);
        _watchers.Add(watcher);

        await PushAsync(watcher)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal void Unwatch(IJournalObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _watchers.RemoveAll(existing => existing.Observer.Equals(observer));
    }

    internal JournalWindowCheckpoint IncomingCheckpoint() => _incoming.Checkpoint();

    internal JournalWindowCheckpoint OutgoingCheckpoint() => _outgoing.Checkpoint();

    internal void AppendIncoming(SignalDelivery delivery) => _incoming.Append(delivery);

    internal void AppendOutgoing(SignalDelivery delivery) => _outgoing.Append(delivery);

    internal void RestoreIncoming(JournalWindowCheckpoint checkpoint) => _incoming.Restore(checkpoint);

    internal void RestoreOutgoing(JournalWindowCheckpoint checkpoint) => _outgoing.Restore(checkpoint);

    internal async Task NotifyWatchersAsync()
    {
        foreach (var watcher in _watchers.ToArray())
        {
            try
            {
                await PushAsync(watcher)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            catch (Exception unreachable)
            {
                _watchers.Remove(watcher);
                SignalTelemetry.WatcherDropped(_neuronId, unreachable);
            }
        }
    }

    private async Task PushAsync(Watcher watcher)
    {
        var read = WindowFor(watcher.Kind).Read(watcher.Cursor);

        if (read.Delta.Count == 0 && (read.UnknownEntries?.Count ?? 0) == 0 && read.ResetSnapshot is null)
        {
            return;
        }

        watcher.Cursor = read.ResumeSequence;

        await watcher.Observer.ObserveAsync(watcher.Kind, read)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private JournalWindow WindowFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => _incoming,
        JournalKind.Outgoing => _outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class Watcher(IJournalObserver observer, JournalKind kind, long cursor)
    {
        internal IJournalObserver Observer { get; } = observer;

        internal JournalKind Kind { get; } = kind;

        internal long Cursor { get; set; } = cursor;
    }
}
