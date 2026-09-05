using System.Runtime.CompilerServices;
using System.Net.ServerSentEvents;
using System.Threading.Channels;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// Journal observers are wakeups, never topology or a work queue. Every reconnect
// starts with an authoritative snapshot. The lease renews observers after grain
// deactivation; it does not repeatedly read the graph while nothing changes.
internal sealed class BrainGraphStream(BrainGraphProjection projection, IBrainGraphSource source, IBrainGraphObservers observers)
{
    public async IAsyncEnumerable<SseItem<BrainGraphSnapshot?>> WatchAsync(string chatName, ActorContext actor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var principal = VerifiedActor.Enter(actor);
        var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        await using var observer = observers.Create(() => changes.Writer.TryWrite(true));
        var snapshot = await projection.ReadAsync(chatName, actor, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            snapshot = await ObserveAsync(snapshot, observer, cancellationToken).ConfigureAwait(false);
            yield return new(snapshot, "brain-snapshot");
            while (!cancellationToken.IsCancellationRequested)
            {
                using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lease.CancelAfter(TimeSpan.FromSeconds(30));
                var changed = false;
                try { changed = await changes.Reader.WaitToReadAsync(lease.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                if (changed || snapshot.Nodes.Any(node => node.Status == "Unavailable"))
                {
                    while (changes.Reader.TryRead(out _)) { }
                    snapshot = await projection.ReadAsync(chatName, actor, cancellationToken).ConfigureAwait(false);
                    break;
                }

                // Renew healthy observers without rereading an idle graph. A failed
                // renewal produces a partial snapshot and is retried next lease.
                snapshot = await ObserveAsync(snapshot, observer, cancellationToken).ConfigureAwait(false);
                if (snapshot.Nodes.Any(node => node.Status == "Unavailable"))
                {
                    yield return new(snapshot, "brain-snapshot");
                }
                else
                {
                    yield return new(null, "brain-heartbeat");
                }
            }
        }
    }

    private async Task<BrainGraphSnapshot> ObserveAsync(BrainGraphSnapshot snapshot,
        IBrainGraphObserver observer, CancellationToken cancellationToken)
    {
        var nodes = snapshot.Nodes.ToArray();
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (node.Status == "Unavailable" || !NeuronId.TryParseInstance(node.Id, source.Owner, out var id)) { continue; }
            try
            {
                await observer.WatchAsync(id, node.IncomingSequence, node.OutgoingSequence, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                nodes[index] = node with { Status = "Unavailable" };
            }
        }
        return snapshot with { Nodes = nodes };
    }
}

internal interface IBrainGraphObservers
{
    IBrainGraphObserver Create(Action changed);
}

internal interface IBrainGraphObserver : IAsyncDisposable
{
    Task WatchAsync(NeuronId neuron, long incomingSequence, long outgoingSequence, CancellationToken cancellationToken);
}

internal sealed class BrainGraphObservers(IGrainFactory grains) : IBrainGraphObservers
{
    public IBrainGraphObserver Create(Action changed) => new Observation(grains, changed);

    private sealed class Observation : IBrainGraphObserver
    {
        private readonly IGrainFactory _grains;
        // Orleans retains observer targets weakly. Keep the actual callback alive
        // for the entire HTTP stream, not only its remote object reference.
        private readonly Observer _observer;
        private readonly IJournalObserver _reference;
        private readonly HashSet<NeuronId> _watched = [];

        public Observation(IGrainFactory grains, Action changed)
        {
            _grains = grains;
            _observer = new Observer(changed);
            _reference = grains.CreateObjectReference<IJournalObserver>(_observer);
        }

        public async Task WatchAsync(NeuronId neuron, long incomingSequence, long outgoingSequence, CancellationToken cancellationToken)
        {
            _watched.Add(neuron);
            var query = _grains.GetGrain<INeuronQuery>(neuron.ToGrainId());
            await query.Watch(JournalKind.Incoming, incomingSequence, _reference).WaitAsync(cancellationToken).ConfigureAwait(false);
            await query.Watch(JournalKind.Outgoing, outgoingSequence, _reference).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var id in _watched)
            {
                try { await _grains.GetGrain<INeuronQuery>(id.ToGrainId()).Unwatch(_reference).WaitAsync(cleanup.Token).ConfigureAwait(false); }
                catch (Exception) { /* Deleting the reference also invalidates observers on unreachable silos. */ }
            }
            _grains.DeleteObjectReference<IJournalObserver>(_reference);
            GC.KeepAlive(_observer);
        }
    }

    private sealed class Observer(Action changed) : IJournalObserver
    {
        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            changed();
            return Task.CompletedTask;
        }
    }
}
