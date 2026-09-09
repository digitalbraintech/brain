using System.Text.Json.Serialization;

namespace DigitalBrain.Product.Identity;

[GenerateSerializer]
[Alias("db.command-id")]
public readonly record struct CommandId
{
    [JsonConstructor]
    public CommandId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A command id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static CommandId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
