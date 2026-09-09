using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.entity-id")]
public readonly record struct EntityId
{
    [JsonConstructor]
    public EntityId(string type, OwnerId owner, string name)
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

    public string GrainKey => $"{Owner.Value}{IdentityPart.OwnerNameSeparator}{Name}";

    public GrainId ToGrainId() => GrainId.Create(Type, GrainKey);

    public static EntityId For<TEntity>(OwnerId owner, string name)
        where TEntity : IEntity
        => new(GrainTypeNames.Of(typeof(TEntity)), owner, name);

    public override string ToString() => $"{Type}:{GrainKey}";
}
