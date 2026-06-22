namespace DigitalBrain.Protocol;

[GenerateSerializer]
public record NeuronId([property: Id(0)] string Value)
{
    public static implicit operator string(NeuronId id) => id.Value;
    public override string ToString() => Value;
}
