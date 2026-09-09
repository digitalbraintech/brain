namespace DigitalBrain.Abstractions.Synapses;

[GenerateSerializer]
[Alias("db.synapse-kind")]
public enum SynapseKind
{
    // Never decays, never pruned, may block. IHandle is capability, not an innate edge.
    Innate,

    // Created by SubscribeTo. Never decays, never pruned, may not block.
    Bound,

    // Created by a successfully handled send. Decays; pruned below the floor.
    Learned,

    // Created by tier-3 similarity search. Decays fastest to nothing; may never block.
    Discovered,
}
