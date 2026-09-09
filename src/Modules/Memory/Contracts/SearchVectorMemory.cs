using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.search-vector")]
public sealed record SearchVectorMemory(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] string Query,
    [property: Id(2)] int Limit,
    [property: Id(3)] IReadOnlyDictionary<string, string>? Metadata) : Signal<VectorMemoryMatches>;

