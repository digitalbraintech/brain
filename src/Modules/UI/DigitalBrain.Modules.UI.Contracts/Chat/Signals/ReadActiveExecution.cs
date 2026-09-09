using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Execution;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.read-active-execution")]
public sealed record ReadActiveExecution : Signal<ActiveExecution>;

[GenerateSerializer]
[Alias("chat.active-execution")]
public sealed record ActiveExecution(
    [property: Id(0)] ExecutionId? ExecutionId) : Signal;
