namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("db.protected-payload-reference")]
public readonly record struct ProtectedPayloadReference
{
    public ProtectedPayloadReference(Guid id, DateTimeOffset? expiresAt = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A protected payload reference cannot be empty.", nameof(id));
        }

        Id = id;
        ExpiresAt = expiresAt;
    }

    [Id(0)]
    public Guid Id { get; }

    [Id(1)]
    public DateTimeOffset? ExpiresAt { get; }

    public override string ToString() => Id.ToString("n");
}
