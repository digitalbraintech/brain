using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.store-vector")]
public sealed record StoreVectorMemory(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] string Key,
    [property: Id(2)] string Text,
    [property: Id(3)] IReadOnlyDictionary<string, string>? Metadata,
    [property: Id(4)] ProtectedPayloadReference? Payload) : Signal<VectorMemoryStored>;

