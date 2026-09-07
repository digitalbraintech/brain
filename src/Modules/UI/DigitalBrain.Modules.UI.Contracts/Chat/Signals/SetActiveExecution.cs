using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.set-active-execution")]
public sealed record SetActiveExecution(
    [property: Id(0)] Guid? ExecutionId) : Signal;
