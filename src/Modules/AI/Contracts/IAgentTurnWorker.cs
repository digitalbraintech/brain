using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

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
        SignalDelivery delivery,
        CancellationToken cancellationToken = default);
}
