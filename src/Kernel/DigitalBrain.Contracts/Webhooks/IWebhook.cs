using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Sdk.Webhooks;

/// <summary>A source-owned, durable subscription endpoint for authenticated external events.</summary>
[Alias("db.webhook")]
public interface IWebhook : INeuron, IHandle<ReadWebhook>, IEmits<WebhookReceived>;

[GenerateSerializer, Alias("db.webhook.read")]
public sealed record ReadWebhook : Signal<WebhookStatus>;

[GenerateSerializer, Alias("db.webhook.status")]
public sealed record WebhookStatus(
    [property: Id(0)] bool Available,
    [property: Id(1)] int PendingReceipts,
    [property: Id(2)] int PendingDeliveries,
    [property: Id(3)] DateTimeOffset? LastAcceptedAt,
    [property: Id(4)] string? Detail = null) : Signal;

/// <summary>Optional common subscription vocabulary. Provider facts can also be subscribed directly.</summary>
[GenerateSerializer, Alias("db.webhook.received")]
public sealed record WebhookReceived(
    [property: Id(0)] string EventId,
    [property: Id(1)] Signal Fact,
    [property: Id(2)] DateTimeOffset ReceivedAt) : Signal;
