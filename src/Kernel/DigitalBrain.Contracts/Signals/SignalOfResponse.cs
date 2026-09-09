namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.request-signal")]
public abstract record Signal<TResponse> : Signal
    where TResponse : Signal;
