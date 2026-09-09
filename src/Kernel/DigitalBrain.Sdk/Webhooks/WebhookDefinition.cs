namespace DigitalBrain.Sdk.Webhooks;

/// <summary>A provider's exact HTTP entry point. Authentication and durable acceptance belong to its handler.</summary>
public sealed record WebhookDefinition(
    string Path,
    int MaxBodyBytes = 1_048_576,
    TimeSpan? AcceptanceTimeout = null);

public sealed record WebhookRequest(
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string[]> Headers);

public enum WebhookAcceptance
{
    Accepted,
    Duplicate,
    Ignored,
    BadRequest,
    Unauthorized,
    Conflict,
    Unavailable,
}

public interface IWebhookHandler
{
    /// <summary>Return Accepted or Duplicate only after the receipt is durable. Do not await downstream work.</summary>
    Task<WebhookAcceptance> HandleAsync(WebhookRequest request, CancellationToken cancellationToken);
}
