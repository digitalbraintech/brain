using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.vector-stored")]
public sealed record VectorMemoryStored(
    [property: Id(0)] bool Stored,
    [property: Id(1)] VectorMemoryNamespace Namespace,
    [property: Id(2)] string Key,
    [property: Id(3)] VectorMemoryStoreStatus Status) : Signal;

