using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// Singleton fanout for RfwCards — the server-driven-UI backbone. Each WatchHomeFeed gRPC subscriber opens its own
// unbounded channel; neurons broadcast RfwCards to all subscribers. A bounded content-hash dedup window avoids
// re-pushing identical cards. Harvested from digitalbrain HomeFeedBus; the ConversationGrain persistence is
// dropped (MAIN keeps chat history in the ChatNeuron journal).
public sealed class HomeFeedBus
{
    private const int MaxSeenEntries = 5_000;
    private readonly ConcurrentDictionary<Guid, Channel<RfwCard>> _subscribers = new();
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _subscribers[id] = channel;
        return new Subscription(this, id, channel);
    }

    public void Broadcast(RfwCard card)
    {
        if (IsDuplicate(card)) return;
        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryWrite(card);
    }

    private bool IsDuplicate(RfwCard card)
    {
        var key = $"{card.CorrelationId}|{ContentHash(card)}";
        lock (_seenLock)
        {
            if (!_seen.Add(key)) return true;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > MaxSeenEntries)
                _seen.Remove(_seenOrder.Dequeue());
            return false;
        }
    }

    private static string ContentHash(RfwCard card) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{card.LibraryName}|{card.RootWidget}|{card.DataJson}")));

    public sealed class Subscription(HomeFeedBus owner, Guid id, Channel<RfwCard> channel) : IDisposable
    {
        public ChannelReader<RfwCard> Reader { get; } = channel.Reader;

        public void Dispose()
        {
            if (owner._subscribers.TryRemove(id, out _))
                channel.Writer.TryComplete();
        }
    }
}
