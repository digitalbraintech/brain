namespace DigitalBrain.Protocol;

[GenerateSerializer]
public record NeuronActivated(NeuronId Neuron) : Synapse(nameof(NeuronActivated), DateTimeOffset.UtcNow);
