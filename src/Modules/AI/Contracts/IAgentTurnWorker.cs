using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.AI;

[Alias("db.agent-turn-worker")]
public interface IAgentTurnWorker : IGrainWithStringKey
{
    const string GrainTypeName = "agent-turn-worker";

    static string KeyFor(OwnerId owner, CorrelationId correlation)
        => $"{owner.Value}/{correlation}";

    [Alias(nameof(Enqueue))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Enqueue(
        CorrelationId correlation,
        CommandId command,
        string text,
        ActorContext? actor,
        CancellationToken cancellationToken = default);
}
