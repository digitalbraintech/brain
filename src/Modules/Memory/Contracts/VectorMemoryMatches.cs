using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.vector-matches")]
public sealed record VectorMemoryMatches(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] IReadOnlyList<VectorMemoryMatch> Matches) : Signal;

