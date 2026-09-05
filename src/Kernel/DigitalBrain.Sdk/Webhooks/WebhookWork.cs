using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Sdk.Webhooks;

// Hosting protocol, deliberately separate from IWebhook: scripts cannot inject authenticated receipts.
[Alias("sdk.webhook.ingress")]
public interface IAuthenticatedWebhookIngress : IGrainWithStringKey
{
    Task<WebhookAcceptance> AcceptAsync(WebhookReceipt receipt);
    Task<WebhookWork?> ClaimAsync();
    Task CompleteAsync(Guid lease, Signal[] results);
    Task AcknowledgeAsync(Guid lease, bool handled);
    Task FailAsync(Guid lease);
}

[GenerateSerializer, Alias("sdk.webhook.receipt")]
public sealed record WebhookReceipt(
    [property: Id(0)] string DeliveryId,
    [property: Id(1)] string Digest,
    [property: Id(2)] string Epoch,
    [property: Id(3)] Signal Input,
    [property: Id(4)] DateTimeOffset AcceptedAt,
    [property: Id(5)] string? TraceParent = null,
    [property: Id(6)] string? TraceState = null);

[GenerateSerializer, Alias("sdk.webhook.work")]
public sealed record WebhookWork(
    [property: Id(0)] Guid Lease,
    [property: Id(1)] WebhookReceipt? Receipt,
    [property: Id(2)] SignalDelivery? Delivery,
    [property: Id(3)] NeuronId? Recipient,
    [property: Id(4)] bool IsReply = false,
    [property: Id(5)] long? FenceEpoch = null);

public interface IWebhookProcessor
{
    bool Handles(NeuronId source);
    Task<Signal[]> ProcessAsync(NeuronId source, WebhookReceipt receipt, CancellationToken cancellationToken);
}

[GenerateSerializer, Alias("sdk.webhook.pending-receipt")]
internal sealed record PendingWebhookReceipt(
    [property: Id(0)] WebhookReceipt Receipt,
    [property: Id(1)] bool Completed = false,
    [property: Id(2)] Guid Lease = default,
    [property: Id(3)] DateTimeOffset? DueAt = null,
    [property: Id(4)] int Attempts = 0);

[GenerateSerializer, Alias("sdk.webhook.recipient")]
internal sealed record WebhookRecipient(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] Guid Lease = default,
    [property: Id(2)] DateTimeOffset? DueAt = null,
    [property: Id(3)] int Attempts = 0);

[GenerateSerializer, Alias("sdk.webhook.pending-delivery")]
internal sealed record PendingWebhookDelivery(
    [property: Id(0)] string EventId,
    [property: Id(1)] Signal Signal,
    [property: Id(2)] WebhookRecipient[] Recipients,
    [property: Id(3)] SignalDelivery? Delivery = null,
    [property: Id(4)] SignalDelivery? ReplyTo = null,
    [property: Id(5)] string? Epoch = null,
    [property: Id(6)] long Generation = 0);

[GenerateSerializer, Alias("sdk.webhook.fence")]
internal sealed record PendingWebhookFence(
    [property: Id(0)] NeuronId Recipient,
    [property: Id(1)] long Generation,
    [property: Id(2)] Guid Lease = default,
    [property: Id(3)] DateTimeOffset? DueAt = null,
    [property: Id(4)] int Attempts = 0);

[GenerateSerializer, Alias("sdk.webhook.state")]
internal sealed record WebhookState
{
    [Id(0)] public List<PendingWebhookReceipt> Receipts { get; init; } = [];
    [Id(1)] public List<PendingWebhookDelivery> Deliveries { get; init; } = [];
    [Id(2)] public DateTimeOffset? LastAcceptedAt { get; init; }
    [Id(3)] public string? Epoch { get; init; }
    [Id(4)] public long Generation { get; init; }
    [Id(5)] public bool Available { get; init; }
    [Id(6)] public List<PendingWebhookFence> Fences { get; init; } = [];
}
