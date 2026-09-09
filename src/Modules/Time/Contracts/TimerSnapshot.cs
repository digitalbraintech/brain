using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-snapshot")]
public sealed record TimerSnapshot(
    [property: Id(0)] TimerStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset? ScheduledAt,
    [property: Id(3)] DateTimeOffset? DueAt,
    [property: Id(4)] TimeSpan? Duration,
    [property: Id(5)] string? Note) : Signal;

