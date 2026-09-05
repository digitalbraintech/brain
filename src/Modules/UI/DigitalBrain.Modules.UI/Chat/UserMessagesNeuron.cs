using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("usermessages")]
internal sealed class UserMessagesNeuron(NeuronRuntime runtime) : Neuron(runtime), IUserMessages
{
    public Task HandleAsync(UserMessaged signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var cause = CurrentDelivery ?? throw new InvalidOperationException("UserMessages requires a delivery.");
        return BroadcastAsync(signal, cause);
    }
}
