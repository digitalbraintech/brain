using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Core;

// The six constants that govern how the graph learns and forgets. Registered as a singleton by
// DigitalBrainRuntime.Add so a host or a test can replace the whole set.
public sealed class SynapseOptions
{
    // w' = w + rate * (1 - w). Asymptotic to 1, so a weight can never reach or exceed it.
    public double PotentiationRate { get; init; } = 0.30;

    public double InitialLearnedWeight { get; init; } = 0.50;

    // Tier-3 routes start weak and must earn their place through use (spec 5.1).
    public double InitialDiscoveredWeight { get; init; } = 0.10;

    public double InnateWeight { get; init; } = 1.00;

    public TimeSpan HalfLife { get; init; } = TimeSpan.FromDays(14);

    public double PruneFloor { get; init; } = 0.05;

    public double InitialWeightFor(SynapseKind kind) => kind switch
    {
        SynapseKind.Innate => InnateWeight,
        SynapseKind.Bound => InnateWeight,
        SynapseKind.Learned => InitialLearnedWeight,
        SynapseKind.Discovered => InitialDiscoveredWeight,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
