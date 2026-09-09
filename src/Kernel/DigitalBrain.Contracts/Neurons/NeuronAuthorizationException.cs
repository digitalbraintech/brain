namespace DigitalBrain.Abstractions.Neurons;

[GenerateSerializer]
[Alias("db.authorization-error")]
public sealed class NeuronAuthorizationException : Exception
{
    public NeuronAuthorizationException()
        : this("The caller is not authorized to reach this neuron.")
    {
    }

    public NeuronAuthorizationException(string message)
        : base(message)
    {
    }

    public NeuronAuthorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
