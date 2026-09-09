using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.journal-entry")]
internal sealed record JournalEntry(
    [property: Id(0)] long Sequence,
    [property: Id(1)] SignalDelivery Delivery);
