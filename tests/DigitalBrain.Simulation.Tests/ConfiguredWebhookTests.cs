using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Webhooks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ConfiguredWebhookTests
{
    [Fact]
    public async Task GenericAuthenticatedWebhookReachesTypedBehaviorExactlyOnce()
    {
        var token = TestContext.Current.CancellationToken;
        var principal = PrincipalId.New();
        const string secret = "test-signing-secret-with-thirty-two-characters";
        var registration = new ConfiguredWebhookSource(new(DigitalBrainNames.DefaultOwner), principal, "build-events", "/test/build-events", secret);
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]), ConfigureSilo = silo => silo.Services.AddWebhookSource(registration),
        });
        using var actor = VerifiedActor.Enter(new(principal, "test"));
        var source = sim.Brain.Get<IWebhook>("build-events");
        var behavior = sim.Brain.Get<IBehavior>("build-notifier");
        var kernel = sim.Grains.GetGrain<IBehaviorKernel>(behavior.Id.ToGrainId());
        var saved = await behavior.RequestAsync(new SaveBehaviorScript("return null;", [nameof(WebhookReceived)], []), token);
        await kernel.ValidateDraft(saved.Behavior.Draft!.Revision, []);
        await behavior.SubscribeAsync(source, token);
        await behavior.ActivateAsync(token);
        var handler = new ConfiguredWebhookHandler(registration, sim.Grains);
        var accepted = Request("build-42", "{\"build\":42}");
        Assert.Equal(WebhookAcceptance.Accepted, await handler.HandleAsync(accepted, token));
        Assert.Equal(WebhookAcceptance.Duplicate, await handler.HandleAsync(accepted, token));
        Assert.Equal(WebhookAcceptance.Conflict, await handler.HandleAsync(Request("build-42", "{\"build\":43}"), token));
        Assert.Equal(WebhookAcceptance.Unauthorized, await handler.HandleAsync(accepted with
        {
            Headers = new Dictionary<string, string[]> { ["X-DigitalBrain-Delivery"] = ["build-42"] },
        }, token));
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        while ((await kernel.ReadState()).PendingCount == 0 && DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(50, token);
        }
        Assert.Equal(1, (await kernel.ReadState()).PendingCount);
        var claim = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        var input = Assert.IsType<WebhookReceived>(claim.Input.Signal);
        Assert.Equal("build-42", input.EventId);
        Assert.Equal("{\"build\":42}", Assert.IsType<WebhookPayload>(input.Fact).Json);
        Assert.Equal(source.Id, claim.Input.Caller);

        static WebhookRequest Request(string eventId, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes));
            return new(bytes, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-DigitalBrain-Delivery"] = [eventId], ["X-DigitalBrain-Signature-256"] = ["sha256=" + signature],
            });
        }
    }
}
