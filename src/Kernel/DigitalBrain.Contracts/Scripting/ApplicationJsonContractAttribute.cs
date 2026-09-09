namespace DigitalBrain.Abstractions.Scripting;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ApplicationJsonContractAttribute(string stableName, int schemaVersion) : Attribute
{
    public string StableName { get; } = string.IsNullOrWhiteSpace(stableName)
        ? throw new ArgumentException("A stable application contract name is required.", nameof(stableName))
        : stableName;
    public int SchemaVersion { get; } = schemaVersion > 0
        ? schemaVersion
        : throw new ArgumentOutOfRangeException(nameof(schemaVersion));
}
