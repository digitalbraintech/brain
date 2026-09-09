using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.vector-match")]
public sealed record VectorMemoryMatch(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] IReadOnlyDictionary<string, string> Metadata,
    [property: Id(3)] ProtectedPayloadReference? Payload);

