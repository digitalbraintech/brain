using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// A neuron is an addressable graph endpoint. IGrainWithStringKey is Orleans hosting;
// product verbs (publish, broadcast, subscribe) live on Neuron and NeuronReference.
[Alias("db.v2.neuron")]
public interface INeuron : IGrainWithStringKey, IHandle<Subscribe>, IHandle<Unsubscribe>;
