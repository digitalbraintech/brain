using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-cancelled")]
public sealed record TimerCancelled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Timer,
    [property: Id(2)] long Generation) : Signal;

