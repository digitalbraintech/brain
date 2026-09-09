using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.vector-removed")]
public sealed record VectorMemoryRemoved(
    [property: Id(0)] bool Removed,
    [property: Id(1)] VectorMemoryNamespace Namespace,
    [property: Id(2)] string Key) : Signal;

