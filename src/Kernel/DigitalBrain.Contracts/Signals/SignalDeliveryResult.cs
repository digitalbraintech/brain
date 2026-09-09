namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.v2.signal-delivery-result")]
public sealed record SignalDeliveryResult(
    [property: Id(0)] SignalDelivery Delivery,
    [property: Id(1)] DeliveryOutcome Outcome);
