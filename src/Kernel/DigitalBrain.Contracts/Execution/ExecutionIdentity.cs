using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Execution;

[GenerateSerializer]
[Alias("db.ctx.execution-identity")]
public sealed record ExecutionIdentity(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] OwnerId Owner,
    [property: Id(2)] PrincipalId Principal,
    [property: Id(3)] CorrelationId CorrelationId,
    [property: Id(4)] SignalId CausingMessageId,
    [property: Id(5)] NeuronId Caller,
    [property: Id(6)] ExecutionId? ParentExecutionId,
    [property: Id(7)] string DefinitionKey,
    [property: Id(8)] string DefinitionRevision,
    [property: Id(9)] string ActivationRevision);
