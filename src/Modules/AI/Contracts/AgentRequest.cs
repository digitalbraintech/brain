using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("db.agent-request")]
public sealed record AgentRequest(
    [property: Id(0)] string Text) : Signal<AgentReply>, ICheckpointedRequest;

[GenerateSerializer]
[Alias("db.agent-reply")]
public sealed record AgentReply(
    [property: Id(0)] string Text) : Signal;
