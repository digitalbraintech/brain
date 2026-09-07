using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.signal-id")]
public readonly record struct SignalId
{
    [JsonConstructor]
    public SignalId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A signal id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static SignalId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
