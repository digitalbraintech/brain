namespace DigitalBrain.Abstractions.Neurons;

[Alias("handler")]
public interface IHandler : INeuron
{
    const string GrainTypeName = "handler";
}
