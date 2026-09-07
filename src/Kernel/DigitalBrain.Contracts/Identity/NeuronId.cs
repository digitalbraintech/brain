using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.neuron-id")]
public readonly record struct NeuronId
{
    public const string PlainType = "neuron";

    [JsonConstructor]
    public NeuronId(string type, string name)
    {
        Type = IdentityPart.Validated(type, nameof(type)).ToLowerInvariant();
        Name = IdentityPart.Validated(name, nameof(name));
    }

    [Id(0)] public string Type { get; }

    [Id(1)] public string Name { get; }

    public GrainId ToGrainId() => GrainId.Create(Type, Name);

    public static NeuronId Plain(string name) => new(PlainType, name);

    public static NeuronId FromGrainId(GrainId id) => new(id.Type.ToString()!, id.Key.ToString()!);

    // "type:name" or bare "name" (a plain neuron).
    public static bool TryParse(string? text, out NeuronId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        try
        {
            id = separator < 0
                ? Plain(trimmed)
                : new NeuronId(trimmed[..separator], trimmed[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override string ToString() => $"{Type}:{Name}";
}
