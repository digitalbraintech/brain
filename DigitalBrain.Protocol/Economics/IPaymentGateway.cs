namespace DigitalBrain.Protocol;

// Payment abstraction for premium marketplace purchases. Implemented by a synthetic dev gateway and a real
// Stripe gateway (selected by config). A confirmed payment leads to a license being issued for the buyer.
public interface IPaymentGateway
{
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default);

    // Verify and interpret a provider webhook (or a synthetic confirmation) — a completed payment carries the
    // bundle + user so the caller can issue the license.
    PaymentConfirmation VerifyWebhook(string payloadJson, string? signatureHeader);
}

public sealed record CheckoutRequest(
    string BundleId,
    string UserId,
    string ProductName,
    decimal Price,
    string SuccessUrl,
    string CancelUrl);

public sealed record CheckoutSession(string SessionId, string Url);

public sealed record PaymentConfirmation(bool Completed, string? SessionId, string? BundleId, string? UserId, string Reason);
