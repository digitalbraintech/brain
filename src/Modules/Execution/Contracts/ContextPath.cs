using System.Text.Json.Serialization;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.context-path")]
public readonly record struct ContextPath
{
    [JsonConstructor]
    public ContextPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A context path is required.", nameof(value));
        }

        var normalized = value.Trim().Trim('/');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A context path is required.", nameof(value));
        }

        Value = normalized;
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
