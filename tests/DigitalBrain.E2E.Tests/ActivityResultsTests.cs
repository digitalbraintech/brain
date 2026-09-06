using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Kernel;
using DigitalBrain.Product.Identity;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.E2E.Tests;

public sealed class ActivityResultsTests
{
    [Fact]
    public void Equal_timestamps_keep_the_causal_input_before_its_response()
    {
        var principal = PrincipalId.New();
        var source = new NeuronId("assistant", new OwnerId("result-test"), "assistant");
        var command = CommandId.New();
        var clock = new FixedClock();
        var input = SignalDelivery.Create(new UserMessaged(command, source, "question"), source, 1, clock,
            principal: principal, signalId: new SignalId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        var response = SignalDelivery.Create(new Responded(command, source, "answer"), source, 2, clock,
            cause: input, signalId: new SignalId(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        var activity = Activity(input, principal);
        var results = ActivityResultsHttpMaps.Project([response, input], activity, principal);
        Assert.Equal(["question", "answer"], results.Select(item => item.Text));
    }

    [Fact]
    public void System_results_are_visible_without_exposing_foreign_or_telemetry_reactions()
    {
        var principal = PrincipalId.New();
        var source = new NeuronId("timer", new OwnerId("result-test"), "daily");
        var system = SignalDelivery.Create(new Note("scheduled result"), source, 1, TimeProvider.System);
        var privateResult = SignalDelivery.Create(new Note("someone else's result"), source, 2, TimeProvider.System,
            correlation: system.CorrelationId, principal: PrincipalId.New());
        var telemetry = SignalDelivery.Create(new ReadActivities(), source, 3, TimeProvider.System,
            correlation: system.CorrelationId);
        var reaction = SignalDelivery.Create(new Note("observer reaction"), source, 4, TimeProvider.System, cause: telemetry);
        var activity = Activity(system, null);
        Assert.Equal("scheduled result", Assert.Single(ActivityResultsHttpMaps.Project(
            [reaction, privateResult, system], activity, principal)).Text);
        Assert.Empty(ActivityResultsHttpMaps.Project([system], activity with { Principal = principal }, principal));
    }

    [Fact]
    public async Task Compacted_journals_recover_the_retained_results_from_the_reset_boundary()
    {
        var source = new NeuronId("assistant", new OwnerId("result-test"), "assistant");
        var retained = SignalDelivery.Create(new Note("recent answer"), source, 600, TimeProvider.System);
        var cursors = new List<long>();
        var results = await ActivityResultsHttpMaps.ReadRetainedAsync(cursor =>
        {
            cursors.Add(cursor);
            return Task.FromResult(cursor == 0
                ? new JournalRead(600, [], new JournalSnapshot(600, 600, 89, 512, []))
                : new JournalRead(600, [retained], null));
        }, TestContext.Current.CancellationToken);
        Assert.Equal([0L, 88L], cursors);
        Assert.Equal(retained, Assert.Single(results));
    }

    [Fact]
    public async Task Repeated_compaction_is_bounded_and_never_becomes_an_empty_success()
    {
        var calls = 0;
        await Assert.ThrowsAsync<ActivityResultsUnavailableException>(() => ActivityResultsHttpMaps.ReadRetainedAsync(_ =>
        {
            calls++;
            return Task.FromResult(new JournalRead(1000 + calls, [], new JournalSnapshot(1000 + calls, 1000 + calls, 500 + calls, 501, [])));
        }, TestContext.Current.CancellationToken));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task More_than_64_participants_are_read_with_bounded_concurrency()
    {
        var active = 0;
        var peak = 0;
        var calls = 0;
        var source = new NeuronId("assistant", new OwnerId("result-test"), "assistant");
        var results = await ActivityResultsHttpMaps.ReadParticipantsAsync(
            Enumerable.Range(0, 100).Select(index => $"worker:{index}"), async (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                var count = Interlocked.Increment(ref active);
                int observed;
                do
                {
                    observed = Volatile.Read(ref peak);
                } while (count > observed && Interlocked.CompareExchange(ref peak, count, observed) != observed);
                await Task.Yield();
                Interlocked.Decrement(ref active);
                Interlocked.Increment(ref calls);
                return [SignalDelivery.Create(new Note("result"), source, 1, TimeProvider.System)];
            }, TestContext.Current.CancellationToken);
        Assert.Equal(100, calls);
        Assert.Equal(100, results.Count);
        Assert.InRange(peak, 1, 8);
    }

    [Fact]
    public void Result_projection_keeps_causal_messages_and_excludes_other_activities_and_principals()
    {
        var principal = PrincipalId.New();
        var other = PrincipalId.New();
        var source = new NeuronId("assistant", new OwnerId("result-test"), "assistant");
        var correlation = CorrelationId.New();
        var command = CommandId.New();
        var input = SignalDelivery.Create(new UserMessaged(command, source, "research"), source, 1, TimeProvider.System,
            correlation: correlation, principal: principal);
        var forwarded = SignalDelivery.Create(input.Signal, source, 2, TimeProvider.System, cause: input);
        var card = new KitCardOffer(KitCardKinds.Chart, "research-chart", "Research sources");
        var response = SignalDelivery.Create(new Responded(command, source, "answer", Cards: [card]), source, 3, TimeProvider.System, cause: input);
        var foreign = SignalDelivery.Create(new Note("private"), source, 4, TimeProvider.System,
            correlation: correlation, principal: other);
        var unrelated = SignalDelivery.Create(new Note("unrelated"), source, 5, TimeProvider.System, principal: principal);
        var activity = new ActivityView(correlation.ToString(), correlation.ToString(), input.SignalId.ToString(),
            "UserMessaged", "research", "completed", input.Timestamp, response.Timestamp, [], [], Principal: principal);
        var results = ActivityResultsHttpMaps.Project([response, input, forwarded, response, foreign, unrelated], activity, principal);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].FromUser);
        Assert.Equal("research", results[0].Text);
        Assert.Equal("answer", results[1].Text);
        Assert.Equal(card, Assert.Single(results[1].Cards!));
        Assert.Equal(response.SignalId.ToString(), results[1].EventId);
        Assert.All(results, result => Assert.InRange(result.Sequence, 1L << 52, (1L << 53) - 1));
        Assert.Equal(results.Select(result => result.EventId), ActivityResultsHttpMaps.Project(
            [input, response], activity, principal).Select(result => result.EventId));
    }

    private static ActivityView Activity(SignalDelivery root, PrincipalId? principal)
        => new(root.CorrelationId.ToString(), root.CorrelationId.ToString(), root.SignalId.ToString(),
            root.Signal.GetType().Name, "activity", "completed", root.Timestamp, root.Timestamp, [], [], Principal: principal);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    }
}
