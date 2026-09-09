using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.read-timer")]
public sealed record ReadTimer : Signal<TimerSnapshot>;
