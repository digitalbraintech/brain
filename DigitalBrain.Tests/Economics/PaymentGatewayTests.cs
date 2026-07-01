using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Economics;

public class PaymentGatewayTests
{
    [Fact]
    public async Task Synthetic_Gateway_Creates_Session_And_Confirms_Payment()
    {
        var gateway = new SyntheticPaymentGateway();

        var session = await gateway.CreateCheckoutAsync(
            new CheckoutRequest("PackA", "buyer1", "Pack A", 9.99m, "https://ok", "https://cancel"));
        Assert.StartsWith("cs_synthetic_", session.SessionId);
        Assert.Contains(session.SessionId, session.Url);

        var payload = JsonSerializer.Serialize(new { sessionId = session.SessionId, bundleId = "PackA", userId = "buyer1" });
        var confirmation = gateway.VerifyWebhook(payload, signatureHeader: null);

        Assert.True(confirmation.Completed);
        Assert.Equal("PackA", confirmation.BundleId);
        Assert.Equal("buyer1", confirmation.UserId);
    }
}

