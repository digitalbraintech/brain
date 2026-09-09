using System.Text.Json.Serialization;

using DigitalBrain.Abstractions.Neurons;
namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.neuron-id")]
public readonly record struct NeuronId
{
    [JsonConstructor]
    public NeuronId(string type, OwnerId owner, string name)
    {
        Type = IdentityPart.Validated(type, nameof(type)).ToLowerInvariant();
        Owner = owner;
        Name = IdentityPart.Validated(name, nameof(name));
    }

    [Id(0)]
    public string Type { get; }

    [Id(1)]
    public OwnerId Owner { get; }

    [Id(2)]
    public string Name { get; }

    // Hosting key (Orleans). Scripts address neurons with For<TNeuron>, not grain ids.
    public string GrainKey => $"{Owner.Value}{IdentityPart.OwnerNameSeparator}{Name}";

    public GrainId ToGrainId() => GrainId.Create(Type, GrainKey);

    public static NeuronId For<TNeuron>(OwnerId owner, string name)
        where TNeuron : INeuron
        => new(GrainTypeNameOf(typeof(TNeuron)), owner, name);

    public static string GrainTypeNameOf(Type neuronType) => GrainTypeNames.Of(neuronType);

    // The "type:name" instance shape tool surfaces accept. A "type:owner/name" form is
    // refused rather than silently re-owned: the owner always comes from the calling surface.
    public static bool TryParseInstance(string? instance, OwnerId owner, out NeuronId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(instance))
        {
            return false;
        }

        var trimmed = instance.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        try
        {
            id = new NeuronId(trimmed[..separator], owner, trimmed[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static NeuronId FromGrainKey(string type, string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);

        var separator = grainKey.IndexOf(IdentityPart.OwnerNameSeparator, StringComparison.Ordinal);

        if (separator <= 0 || separator == grainKey.Length - 1)
        {
            throw new ArgumentException($"Grain key '{grainKey}' is not in owner/name form.", nameof(grainKey));
        }

        return new NeuronId(type, new OwnerId(grainKey[..separator]), grainKey[(separator + 1)..]);
    }

    public override string ToString() => $"{Type}:{GrainKey}";
}
