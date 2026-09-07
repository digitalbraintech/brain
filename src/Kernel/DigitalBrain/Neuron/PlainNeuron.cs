using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

// The default neuron: a name, synapses, journals, and the latest signal per type. Reacts to nothing.
[GrainType(NeuronId.PlainType)]
public sealed class PlainNeuron(NeuronRuntime runtime) : Neuron(runtime);
