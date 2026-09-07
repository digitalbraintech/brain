using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("db.agent-request")]
[ApplicationJsonContract("db.agent-request", 1)]
public sealed record AgentRequest(
    [property: Id(0)] string Text) : Signal<AgentReply>, ICheckpointedRequest;

[GenerateSerializer]
[Alias("db.agent-reply")]
[ApplicationJsonContract("db.agent-reply", 1)]
public sealed record AgentReply(
    [property: Id(0)] string Text) : Signal;
