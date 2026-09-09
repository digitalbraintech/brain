namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.correlation-id")]
public readonly record struct CorrelationId
{
    public CorrelationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A correlation id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static CorrelationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
