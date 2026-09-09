using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.remove-vector")]
public sealed record RemoveVectorMemory(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] string Key) : Signal<VectorMemoryRemoved>;

