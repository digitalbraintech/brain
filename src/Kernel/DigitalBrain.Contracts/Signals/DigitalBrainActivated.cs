using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.digitalbrain-activated")]
public sealed record DigitalBrainActivated([property: Id(0)] OwnerId Owner) : Signal;
