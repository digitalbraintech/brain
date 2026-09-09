namespace DigitalBrain.Abstractions.Signals;

// Typed payload that crosses a synapse. Envelope (id, correlation, caller) lives on SignalDelivery.
[GenerateSerializer]
[Alias("db.signal")]
public abstract record Signal;
