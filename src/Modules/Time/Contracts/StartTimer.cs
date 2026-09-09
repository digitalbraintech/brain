using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.start-timer")]
public sealed record StartTimer(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] int DurationSeconds,
    [property: Id(2)] string Note) : Signal<TimerScheduled>;

