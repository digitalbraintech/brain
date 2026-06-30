using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// Singleton egress fanout for broadcast Signals — the outbound mirror of generic Send. Each WatchSynapses gRPC
// subscriber opens its own unbounded channel with a type-name filter; the per-silo SignalEgressStreamSubscriber
// forwards every Signal it reads off the DigitalBrainTimeline stream into Publish, which fans to the subscribers
// whose filter accepts it. An empty/null filter receives all Signals.
public sealed class SignalEgressBus
{
    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();

    public Subscription Subscribe(IReadOnlyCollection<string>? typeFilter)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<Signal>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var filter = typeFilter is { Count: > 0 }
            ? new HashSet<string>(typeFilter, StringComparer.Ordinal)
            : null;
        _subscribers[id] = new Subscriber(channel, filter);
        return new Subscription(this, id, channel);
    }

    // Called by SignalEgressStreamSubscriber on each silo to fan a timeline Signal to local matching subscribers.
    public void Publish(Signal signal)
    {
        foreach (var (_, subscriber) in _subscribers)
        {
            if (subscriber.Filter is null || subscriber.Filter.Contains(signal.Name))
                subscriber.Channel.Writer.TryWrite(signal);
        }
    }

    private sealed record Subscriber(Channel<Signal> Channel, HashSet<string>? Filter);

    public sealed class Subscription(SignalEgressBus owner, Guid id, Channel<Signal> channel) : IDisposable
    {
        public ChannelReader<Signal> Reader { get; } = channel.Reader;

        public void Dispose()
        {
            if (owner._subscribers.TryRemove(id, out _))
                channel.Writer.TryComplete();
        }
    }
}
