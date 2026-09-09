using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.cancel-timer")]
public sealed record CancelTimer(
    [property: Id(0)] CommandId CommandId) : Signal<TimerCancelled>;

