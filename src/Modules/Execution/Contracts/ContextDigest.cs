using System.Text.Json.Serialization;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.context-digest")]
public readonly record struct ContextDigest
{
    [JsonConstructor]
    public ContextDigest(string sha256Hex)
    {
        if (string.IsNullOrWhiteSpace(sha256Hex))
        {
            throw new ArgumentException("A context digest is required.", nameof(sha256Hex));
        }

        Sha256Hex = sha256Hex;
    }

    [Id(0)]
    public string Sha256Hex { get; }

    public override string ToString() => Sha256Hex;
}
