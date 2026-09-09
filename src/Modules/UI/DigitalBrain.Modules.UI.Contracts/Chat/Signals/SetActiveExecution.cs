using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Execution;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.set-active-execution")]
public sealed record SetActiveExecution(
    [property: Id(0)] ExecutionId? ExecutionId) : Signal;
