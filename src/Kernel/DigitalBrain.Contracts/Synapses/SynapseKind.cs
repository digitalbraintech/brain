namespace DigitalBrain.Abstractions.Synapses;

[GenerateSerializer]
[Alias("db.synapse-kind")]
public enum SynapseKind
{
    // Never pruned, may block. IHandle is capability, not an innate edge.
    Innate,

    // Created by SubscribeTo. Never pruned, may not block.
    Bound,

    // Created by a successfully handled send. Causal observation, not a subscription.
    Learned,
}
