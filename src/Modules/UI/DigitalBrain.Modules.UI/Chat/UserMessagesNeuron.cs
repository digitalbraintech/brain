using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("usermessages")]
#pragma warning disable CS0618 // IUserMessages remains the compatibility grain interface.
internal sealed class UserMessagesNeuron(NeuronRuntime runtime) : Neuron(runtime), IComposer, IUserMessages
#pragma warning restore CS0618
{
    public Task HandleAsync(UserMessaged signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var cause = CurrentDelivery ?? throw new InvalidOperationException("UserMessages requires a delivery.");
        return BroadcastAsync(signal, cause);
    }
}
