using System.Diagnostics;

namespace DigitalBrain.Sdk.Webhooks;

public static class WebhookTrace
{
    public static readonly ActivitySource Source = new("DigitalBrain.Webhooks");
    public static WebhookReceipt Capture(WebhookReceipt receipt) => Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? Sanitize(receipt with { TraceParent = current.Id, TraceState = current.TraceStateString }) : receipt with
    {
        TraceParent = null,
        TraceState = null
    };
    public static WebhookReceipt Sanitize(WebhookReceipt receipt)
    {
        if (receipt.TraceParent is not { Length: 55 } parent || !ActivityContext.TryParse(parent, null, out var parsed) || parsed.TraceId == default || parsed.SpanId == default)
        {
            return receipt with
            {
                TraceParent = null,
                TraceState = null
            };
        }

        var state = receipt.TraceState;
        if (state is { Length: > 512 } || state?.Any(character => character < 0x20 || character > 0x7e) == true)
        {
            state = null;
        }

        return receipt with
        {
            TraceState = state
        };
    }

    public static Activity? Start(string operation, WebhookReceipt receipt)
    {
        var safe = Sanitize(receipt);
        var parent = ActivityContext.TryParse(safe.TraceParent, safe.TraceState, out var parsed) ? parsed : default;
        return Source.StartActivity(operation, ActivityKind.Consumer, parent)?.SetTag("webhook.delivery.id", receipt.DeliveryId);
    }
}
