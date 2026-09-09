using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.principal-id")]
public readonly record struct PrincipalId
{
    [JsonConstructor]
    public PrincipalId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A principal id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static PrincipalId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
