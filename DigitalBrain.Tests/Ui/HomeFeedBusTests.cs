using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class HomeFeedBusTests
{
    [Fact]
    public async Task Broadcast_Reaches_Subscribers()
    {
        var bus = new HomeFeedBus();
        using var subscription = bus.Subscribe();

        bus.Broadcast(new RfwCard("digitalbrain", "Card", "{\"a\":1}"));

        var received = await subscription.Reader.ReadAsync();
        Assert.Equal("Card", received.RootWidget);
    }

    [Fact]
    public async Task Identical_Cards_Are_Deduped()
    {
        var bus = new HomeFeedBus();
        using var subscription = bus.Subscribe();

        var card = new RfwCard("digitalbrain", "Card", "{\"a\":1}");
        bus.Broadcast(card);
        bus.Broadcast(card); // identical content -> deduped

        Assert.True(subscription.Reader.TryRead(out var first));
        Assert.Equal("Card", first!.RootWidget);
        Assert.False(subscription.Reader.TryRead(out _)); // nothing more
    }
}
