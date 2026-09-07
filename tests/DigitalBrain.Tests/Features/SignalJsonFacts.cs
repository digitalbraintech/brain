using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using Xunit;

namespace DigitalBrain.Tests;

// Durable neuron state (the latest-per-type map, journal windows) is persisted through
// the JSON journal format, so a SignalDelivery must survive System.Text.Json round-tripping.
public sealed class SignalJsonFacts
{
    [Fact]
    public void SignalDeliverySurvivesJsonRoundTrip()
    {
        var delivery = SignalDelivery.Create(
            Signal.Create("Note", """{"t":1}"""),
            NeuronId.Plain("author"),
            sequence: 1,
            TimeProvider.System);

        var json = JsonSerializer.Serialize(delivery);
        var restored = JsonSerializer.Deserialize<SignalDelivery>(json);

        Assert.Equal(delivery, restored);
    }
}
