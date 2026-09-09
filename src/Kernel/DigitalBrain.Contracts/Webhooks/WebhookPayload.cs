using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Sdk.Webhooks;

// A generic provider's bounded JSON. Provider modules can emit richer typed facts instead.
[GenerateSerializer, Alias("db.webhook.payload")]
public sealed record WebhookPayload([property: Id(0)] string Json) : Signal;
