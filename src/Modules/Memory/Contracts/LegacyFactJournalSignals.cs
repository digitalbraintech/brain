using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Memory;

// These records are wire compatibility only. Chat no longer emits fact-memory events and
// there is no fact-memory neuron, but retained journals from earlier versions still contain
// the polymorphic payloads. Removing their serializers makes every replay of that history fail.
[GenerateSerializer]
[Alias("memory.store-fact")]
public sealed record StoreFact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Correlation = null,
    [property: Id(4)] DateTimeOffset? At = null) : Signal<FactStored>;

[GenerateSerializer]
[Alias("memory.fact-stored")]
public sealed record FactStored(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Sequence) : Signal;
