using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// Compile-time permission to receive TSignal. NeuronReference.SendAsync only compiles
// when the target neuron is IHandle<TSignal>.
public interface IHandle<in TSignal>
    where TSignal : Signal
{
    Task HandleAsync(TSignal signal, CancellationToken cancellationToken);
}
