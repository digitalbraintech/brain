namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.v2.delivery-outcome")]
public enum DeliveryOutcome : byte
{
    Handled,
    Unhandled,
    Refused,
}
