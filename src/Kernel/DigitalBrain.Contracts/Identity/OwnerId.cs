using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.owner-id")]
public readonly record struct OwnerId
{
    [JsonConstructor]
    public OwnerId(string value) => Value = IdentityPart.Validated(value, nameof(value));

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
