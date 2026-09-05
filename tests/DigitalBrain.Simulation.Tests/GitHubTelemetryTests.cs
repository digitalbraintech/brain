using System.Collections.Concurrent;
using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk.Webhooks;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubTelemetryTests
{
    [Fact]
    public async Task Delayed_processing_uses_persisted_webhook_context_without_baggage_or_payloads()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DigitalBrain.Webhooks",
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var delivery = Guid.NewGuid().ToString();
        WebhookReceipt receipt;
        ActivityTraceId trace;
        using (var request = new Activity("aspnet.fixture.webhook").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            request.TraceStateString = "vendor=fixture";
            request.AddBaggage("private-payload", "must-not-be-retained");
            trace = request.TraceId;
            Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(delivery), TestContext.Current.CancellationToken));
            receipt = Assert.IsType<WebhookReceipt>((await scenario.Ingress.ClaimAsync())?.Receipt);
        }
        Assert.True(ActivityContext.TryParse(receipt.TraceParent, receipt.TraceState, out var context));
        Assert.Equal(trace, context.TraceId);
        Assert.Equal("vendor=fixture", receipt.TraceState);
        using (var unrelated = new Activity("unrelated.worker.tick").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            unrelated.AddBaggage("worker-secret", "must-not-be-propagated");
            using var processing = WebhookTrace.Start("webhook.process", receipt);
        }
        var process = Assert.Single(stopped, activity => activity.OperationName == "webhook.process");
        Assert.Equal(trace, process.TraceId);
        Assert.Equal(context.SpanId, process.ParentSpanId);
        Assert.Empty(process.Baggage);
        Assert.Equal(delivery, process.GetTagItem("webhook.delivery.id"));
        Assert.DoesNotContain(process.TagObjects, tag => tag.Key.Contains("payload", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-1234567890123456-01")]
    public void Invalid_persisted_parent_cannot_supply_a_trace_or_vendor_state(string? parent)
    {
        var receipt = WebhookTrace.Sanitize(new("delivery", new string('0', 64), "binding", new ReadWebhook(),
            DateTimeOffset.UtcNow, TraceParent: parent, TraceState: "vendor=fixture"));
        Assert.Null(receipt.TraceParent);
        Assert.Null(receipt.TraceState);
    }

    [Fact]
    public void Capture_rejects_hierarchical_context_and_overlong_vendor_state()
    {
        var receipt = new WebhookReceipt("delivery", new string('0', 64), "binding", new ReadWebhook(), DateTimeOffset.UtcNow);
        using (var hierarchical = new Activity("hierarchical").SetIdFormat(ActivityIdFormat.Hierarchical).Start())
        {
            Assert.Null(WebhookTrace.Capture(receipt).TraceParent);
        }
        using var w3c = new Activity("w3c").SetIdFormat(ActivityIdFormat.W3C).Start();
        w3c.TraceStateString = new string('a', 513);
        var captured = WebhookTrace.Capture(receipt);
        Assert.NotNull(captured.TraceParent);
        Assert.Null(captured.TraceState);
    }
}
