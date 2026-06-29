using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

// Singleton fanout for RfwCards — the server-driven-UI backbone. Each WatchHomeFeed gRPC subscriber opens its own
// unbounded channel; neurons broadcast RfwCards to all subscribers. A bounded content-hash dedup window avoids
// re-pushing identical cards.
// With multiple HA silo replicas, Broadcast publishes to a shared Orleans MemoryStream ("HomeFeed") so every silo's
// HomeFeedStreamSubscriber picks it up and fans it locally — delivering to clients connected to any replica.
public sealed class HomeFeedBus
{
    private const int MaxSeenEntries = 5_000;
    private readonly ConcurrentDictionary<Guid, Channel<RfwCard>> _subscribers = new();
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();
    private readonly IClusterClient? _clusterClient;
    private readonly ILogger<HomeFeedBus>? _logger;
    private IAsyncStream<RfwCard>? _stream;

    public HomeFeedBus(IClusterClient? clusterClient = null, ILogger<HomeFeedBus>? logger = null)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _subscribers[id] = channel;
        return new Subscription(this, id, channel);
    }

    // Called by HomeFeedStreamSubscriber on each silo to fan a received stream card to local gRPC subscribers.
    public void FanLocal(RfwCard card)
    {
        if (IsDuplicate(card)) return;
        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryWrite(card);
    }

    public void Broadcast(RfwCard card)
    {
        if (_clusterClient is null)
        {
            // Single-silo / test fallback: fan directly to local subscribers (old behavior).
            FanLocal(card);
            return;
        }

        _ = Task.Run(async () =>
        {
            try { await GetOrCreateStream().OnNextAsync(card); }
            catch (Exception ex) { _logger?.LogError(ex, "HomeFeed stream publish failed"); }
        });
    }

    private IAsyncStream<RfwCard> GetOrCreateStream()
    {
        if (_stream is not null) return _stream;
        var provider = _clusterClient!.GetStreamProvider("HomeFeed");
        _stream = provider.GetStream<RfwCard>(StreamId.Create("homefeed", Guid.Empty));
        return _stream;
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
