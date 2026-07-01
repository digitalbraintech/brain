using DigitalBrain.Core;
using Stripe;
using Stripe.Checkout;

namespace DigitalBrain.Kernel;

// Real Stripe payment gateway, ported from digitalbrain's StripeGateway. Creates Checkout Sessions and verifies
// completion webhooks. Selected only when Stripe:SecretKey is configured (see EconomicsServices); a missing
// webhook secret is allowed but loudly warned (a production brain MUST set Stripe:WebhookSecret).
public sealed class StripePaymentGateway(IConfiguration configuration, ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    private const string CompletedEventType = "checkout.session.completed";
    private const string BundleMetadataKey = "BundleId";
    private const string UserMetadataKey = "UserId";

    private string SecretKey => configuration["Stripe:SecretKey"]
        ?? throw new InvalidOperationException("Stripe:SecretKey is required for the Stripe payment gateway.");
    private string? WebhookSecret => configuration["Stripe:WebhookSecret"];

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, string>
        {
            [BundleMetadataKey] = request.BundleId,
            [UserMetadataKey] = request.UserId,
        };

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ClientReferenceId = request.UserId,
            Metadata = metadata,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)Math.Round(request.Price * 100m, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = request.ProductName,
                            Metadata = metadata,
                        },
                    },
                },
            ],
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, new RequestOptions { ApiKey = SecretKey }, ct);
        logger.LogInformation("Stripe: created checkout session {SessionId} for bundle {BundleId}.", session.Id, request.BundleId);
        return new CheckoutSession(session.Id, session.Url);
    }

    public PaymentConfirmation VerifyWebhook(string payloadJson, string? signatureHeader)
    {
        Event stripeEvent;
        var webhookSecret = WebhookSecret;
        if (!string.IsNullOrWhiteSpace(webhookSecret))
        {
            try
            {
                stripeEvent = EventUtility.ConstructEvent(payloadJson, signatureHeader, webhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                logger.LogWarning(ex, "Stripe: webhook signature verification failed.");
                return new PaymentConfirmation(false, null, null, null, "signature verification failed");
            }
        }
        else
        {
            // No secret to validate against — a production brain MUST set Stripe:WebhookSecret or it will accept
            // forged completion events. Warn loudly so a misconfigured deployment is visible.
            logger.LogWarning("Stripe: no Stripe:WebhookSecret configured — accepting webhook WITHOUT signature verification. Not for production.");
            stripeEvent = EventUtility.ParseEvent(payloadJson, throwOnApiVersionMismatch: false);
        }

        if (string.Equals(stripeEvent.Type, CompletedEventType, StringComparison.Ordinal)
            && stripeEvent.Data.Object is Session session)
        {
            string? bundleId = null;
            string? userId = null;
            session.Metadata?.TryGetValue(BundleMetadataKey, out bundleId);
            session.Metadata?.TryGetValue(UserMetadataKey, out userId);
            return new PaymentConfirmation(true, session.Id, bundleId, userId ?? session.ClientReferenceId, "ok");
        }

        return new PaymentConfirmation(false, null, null, null, $"unhandled event '{stripeEvent.Type}'");
    }
}

