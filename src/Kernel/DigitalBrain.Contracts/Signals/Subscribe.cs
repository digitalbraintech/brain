using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

// Target → source: write a durable Bound synapse source --SignalType--> target.
[GenerateSerializer]
[Alias("db.subscribe")]
public sealed record Subscribe(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] string SignalType,
    [property: Id(2)] CorrelationId? Correlation = null) : Signal;

[GenerateSerializer]
[Alias("db.unsubscribe")]
public sealed record Unsubscribe(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] string SignalType,
    [property: Id(2)] CorrelationId? Correlation = null) : Signal;
