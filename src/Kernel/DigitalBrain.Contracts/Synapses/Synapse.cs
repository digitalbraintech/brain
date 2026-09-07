using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Synapses;

// A directed, typed edge stored on the SOURCE neuron. The only routing fact in the system.
[GenerateSerializer]
[Alias("db.synapse")]
public readonly record struct Synapse(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] NeuronId Target,
    [property: Id(2)] string SignalType,
    [property: Id(3)] DateTimeOffset CreatedAt)
{
    public override string ToString() => $"{Source} --{SignalType}--> {Target}";
}
