using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;

namespace DigitalBrain.AI;

[Alias("DigitalBrain.AI.IAssistant")]
public interface IAssistant : IAgent, IHandle<UserMessaged>, IAssistantTurn;

[Alias("db.assistant-turn")]
public interface IAssistantTurn : IGrainWithStringKey
{
    [Alias(nameof(RecordTurnFact))]
    Task RecordTurnFact(Signal fact, CorrelationId correlation, CancellationToken cancellationToken = default);

    [Alias(nameof(SendFact))]
    Task<DeliveryOutcome> SendFact(
        NeuronId target,
        Signal signal,
        CorrelationId correlation,
        CancellationToken cancellationToken = default);

    [Alias(nameof(RequestSpecialist))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<AgentReply> RequestSpecialist(
        NeuronId target,
        AgentRequest request,
        CorrelationId correlation,
        CancellationToken cancellationToken = default);
}

