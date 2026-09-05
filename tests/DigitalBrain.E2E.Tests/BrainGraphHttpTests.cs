using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Kernel;
using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class BrainGraphHttpTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Behavior_compilation_claim_and_output_invalidate_the_live_graph_without_refresh()
    {
        using var http = fixture.CreateHttpClient("kernel");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var cancellationToken = deadline.Token;
        var name = $"live-{Guid.NewGuid():N}";
        using var response = await http.GetAsync($"/chats/{name}/brain/events",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var events = SseParser.Create(body).EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        Assert.True(await events.MoveNextAsync());
        var initial = JsonSerializer.Deserialize<BrainGraphSnapshot>(events.Current.Data, Json)!;
        Assert.DoesNotContain(initial.Nodes, node => node.Status == "Unavailable");

        const string source = """
            await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken);
            return digitalBrain.Input<Note>();
            """;
        using var saved = await http.PostAsJsonAsync($"/behaviors/{name}/save",
            new { source, inputSignalTypes = new[] { "Note" }, outputSignalTypes = new[] { "Note" } }, cancellationToken);
        saved.EnsureSuccessStatusCode();
        var draft = (await saved.Content.ReadFromJsonAsync<StudioBehavior>(Json, cancellationToken))!;
        Assert.Equal("Pending", draft.Draft?.Validation);

        var compiled = await NextAsync("compilation", snapshot => snapshot.Nodes.Any(node => node.Id == draft.Id)
            && snapshot.Activity.Count(item => item.NeuronId == draft.Id && item.SignalType == "BehaviorStateChanged") >= 2);
        var ready = await http.GetFromJsonAsync<StudioBehavior>($"/behaviors/{name}", Json, cancellationToken);
        Assert.Equal("Valid", ready?.Draft?.Validation);
        Assert.Equal("Compiled revision ready.", ready?.Detail);
        var compiledSequence = compiled.Nodes.Single(node => node.Id == draft.Id).OutgoingSequence;

        using var bound = await http.PostAsJsonAsync($"/chats/{name}/brain/subscriptions",
            new BrainGraphSubscriptionRequest(draft.Id, initial.RootId, "Note", true), cancellationToken);
        bound.EnsureSuccessStatusCode();
        using var enabled = await http.PostAsJsonAsync($"/behaviors/{name}/enable",
            new { expectedDraftRevision = draft.Draft!.Revision }, cancellationToken);
        enabled.EnsureSuccessStatusCode();
        await NextAsync("activation", snapshot => snapshot.Nodes.Any(node => node.Id == draft.Id && node.Status == "Active"
            && node.OutgoingSequence > compiledSequence));

        using var invoked = await http.PostAsJsonAsync($"/behaviors/{name}/invoke",
            new StudioBehaviorCommand(InputType: "Note", Input: JsonSerializer.SerializeToElement(new { text = "live output" })), cancellationToken);
        invoked.EnsureSuccessStatusCode();
        var running = await NextAsync("claim", snapshot => snapshot.Nodes.Any(node => node.Id == draft.Id && node.Status == "Running"));
        var runningSequence = running.Nodes.Single(node => node.Id == draft.Id).OutgoingSequence;
        var completed = await NextAsync("output completion", snapshot =>
        {
            var output = snapshot.Activity.FirstOrDefault(item => item.NeuronId == draft.Id && item.SignalType == "Note");
            return output is not null && snapshot.Nodes.Any(node => node.Id == draft.Id && node.Status == "Active"
                && node.OutgoingSequence > runningSequence)
                && snapshot.Activity.Any(item => item.NeuronId == draft.Id && item.SignalType == "BehaviorStateChanged"
                    && item.Sequence > output.Sequence);
        });
        Assert.Contains(completed.Activity, item => item.NeuronId == draft.Id
            && item.SignalType == "BehaviorStateChanged" && item.Sequence > runningSequence);
        var finished = await http.GetFromJsonAsync<StudioBehavior>($"/behaviors/{name}", Json, cancellationToken);
        Assert.Equal(0, finished?.PendingCount);
        Assert.Equal("Output delivered.", finished?.Detail);

        using var disabled = await http.PostAsJsonAsync($"/behaviors/{name}/disable", new { }, cancellationToken);
        disabled.EnsureSuccessStatusCode();

        async Task<BrainGraphSnapshot> NextAsync(string transition, Func<BrainGraphSnapshot, bool> accept)
        {
            // Every state read follows a live snapshot; there is no graph polling or
            // manual refresh. A healthy idle graph's 30-second lease cannot satisfy
            // this bound, so the test requires real journal invalidations.
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            var observations = new List<string>();
            try
            {
                while (await events.MoveNextAsync())
                {
                    if (events.Current.EventType != "brain-snapshot") { continue; }
                    var snapshot = JsonSerializer.Deserialize<BrainGraphSnapshot>(events.Current.Data, Json)!;
                    Assert.DoesNotContain(snapshot.Nodes, node => node.Status == "Unavailable");
                    var node = snapshot.Nodes.FirstOrDefault(node => node.Id == draft.Id);
                    observations.Add($"{node?.Status ?? "not discovered"}:{node?.OutgoingSequence}:"
                        + string.Join(',', snapshot.Activity.Where(item => item.NeuronId == draft.Id).Select(item => item.SignalType)));
                    if (accept(snapshot)) { return snapshot; }
                }
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                using var diagnostic = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                diagnostic.CancelAfter(TimeSpan.FromSeconds(5));
                var current = await http.GetFromJsonAsync<StudioBehavior>($"/behaviors/{name}", Json, diagnostic.Token);
                var worker = fixture.App.ResourceNotifications.TryGetCurrentState("scripting", out var resource)
                    ? resource.Snapshot.State?.Text : "unknown";
                Assert.Fail($"No live {transition} notification. Draft={current?.Draft?.Validation}; detail={current?.Detail}; scripting={worker}; "
                    + $"observed=[{string.Join(" | ", observations.TakeLast(8))}]");
            }
            throw new InvalidOperationException("The graph stream ended before the behavior state changed.");
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Http_subscription_changes_the_real_source_owned_edge_and_removes_it_completely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");
        var chatName = $"graph-{Guid.NewGuid():N}";
        var path = $"/chats/{chatName}/brain";
        var initial = await http.GetFromJsonAsync<BrainGraphSnapshot>(path, cancellationToken);
        Assert.NotNull(initial);
        Assert.Contains(initial.Nodes, node => node.Id == "assistant:assistant");
        var request = new BrainGraphSubscriptionRequest("assistant:assistant", initial.RootId, "Note", true);

        using var subscribed = await http.PostAsJsonAsync(path + "/subscriptions", request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, subscribed.StatusCode);
        try
        {
            var bound = await http.GetFromJsonAsync<BrainGraphSnapshot>(path, cancellationToken);
            Assert.NotNull(bound);
            var edge = Assert.Single(bound.Synapses, edge => edge.SourceId == request.SourceId
                && edge.TargetId == request.TargetId && edge.SignalType == request.SignalType);
            Assert.Equal("Bound", edge.Kind);
            Assert.True(edge.CanUnsubscribe);
        }
        finally
        {
            using var removed = await http.PostAsJsonAsync(path + "/subscriptions",
                request with { Subscribed = false }, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        }

        var after = await http.GetFromJsonAsync<BrainGraphSnapshot>(path, cancellationToken);
        Assert.NotNull(after);
        Assert.DoesNotContain(after.Synapses, edge => edge.SourceId == request.SourceId
            && edge.TargetId == request.TargetId && edge.SignalType == request.SignalType);
        Assert.Contains(after.Activity, item => item.SignalType == "Unsubscribe");

        var foreign = request with
        {
            TargetId = "chat:" + PrincipalPartition.InstanceName(PrincipalId.New(), chatName),
        };
        using var refused = await http.PostAsJsonAsync(path + "/subscriptions", foreign, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
