using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// Discoverable publication capability. Actual delivery still requires a source-owned synapse.
public interface IEmits<TSignal> where TSignal : Signal;
