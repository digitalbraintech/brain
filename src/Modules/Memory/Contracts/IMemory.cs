using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Memory;

[Alias("memory.snapshot")]
public interface IMemory : IEntity<MemoryState>
{
    [Alias(nameof(Upsert))]
    Task Upsert(MemoryFact fact);

    [Alias(nameof(Remove))]
    Task Remove(string key);
}

[GenerateSerializer, Alias("memory.state")]
public sealed record MemoryState(
    [property: Id(0)] IReadOnlyList<MemoryFact> Facts);

[GenerateSerializer, Alias("memory.fact")]
public sealed record MemoryFact(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] string SourceChat,
    [property: Id(3)] DateTimeOffset RecordedAt,
    [property: Id(4)] string CommandId);

[GenerateSerializer, Alias("memory.updated")]
public sealed record MemoryUpdated(
    [property: Id(0)] string Key,
    [property: Id(1)] string SourceChat) : Signal;
