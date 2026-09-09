using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-scheduled")]
public sealed record TimerScheduled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Timer,
    [property: Id(2)] long Generation,
    [property: Id(3)] DateTimeOffset ScheduledAt,
    [property: Id(4)] DateTimeOffset DueAt,
    [property: Id(5)] TimeSpan Duration,
    [property: Id(6)] string Note) : Signal;

