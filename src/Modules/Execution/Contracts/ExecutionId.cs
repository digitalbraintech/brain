using System.Text.Json.Serialization;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution-id")]
public readonly record struct ExecutionId
{
    [JsonConstructor]
    public ExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An execution id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static ExecutionId New() => new(Guid.NewGuid());

    public static ExecutionId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("n");
}
