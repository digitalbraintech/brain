using System.Text.Json;
using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// Dev/test payment gateway: returns a synthetic checkout session and treats any confirmation payload as a
// completed payment. Used when no Stripe secret is configured. Never contacts a payment provider.
public sealed class SyntheticPaymentGateway : IPaymentGateway
{
    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var sessionId = $"cs_synthetic_{Guid.NewGuid():N}";
        return Task.FromResult(new CheckoutSession(sessionId, $"https://synthetic.local/checkout/{sessionId}"));
    }

    // The "webhook" is a small JSON { sessionId, bundleId, userId } the caller posts to confirm a dev purchase.
    public PaymentConfirmation VerifyWebhook(string payloadJson, string? signatureHeader)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        return new PaymentConfirmation(
            Completed: true,
            SessionId: root.TryGetProperty("sessionId", out var s) ? s.GetString() : null,
            BundleId: root.TryGetProperty("bundleId", out var b) ? b.GetString() : null,
            UserId: root.TryGetProperty("userId", out var u) ? u.GetString() : null,
            Reason: "synthetic-completed");
    }
}
