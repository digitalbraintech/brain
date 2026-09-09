using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions;

public sealed class SignalDeliveryRefusedException : Exception
{
    public SignalDeliveryRefusedException()
        : this("The target neuron refused the signal.")
    {
    }

    public SignalDeliveryRefusedException(string message)
        : base(message)
    {
    }

    public SignalDeliveryRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal SignalDeliveryRefusedException(NeuronId receiver, Type signalType)
        : this($"Neuron '{receiver}' refused request '{signalType.Name}'.")
    {
    }
}
