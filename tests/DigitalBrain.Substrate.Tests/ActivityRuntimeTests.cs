using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer, Alias("db.test.activity-input")]
public sealed record ActivityInput([property: Id(0)] string Text) : Signal;

[Alias("db.test.activity-probe")]
public interface IActivityProbe : INeuron, IHandle<ActivityInput>;

internal sealed class ActivityProbe(NeuronRuntime runtime) : Neuron(runtime), IActivityProbe
{
    public Task HandleAsync(ActivityInput signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

[Alias("db.test.activity-echo")]
public interface IActivityEcho : INeuron, IHandle<ActivityChanged>;

internal sealed class ActivityEcho(NeuronRuntime runtime) : Neuron(runtime), IActivityEcho
{
    public Task HandleAsync(ActivityChanged signal, CancellationToken cancellationToken)
        => BroadcastAsync(new ActivityInput("telemetry reaction"));
}

[Alias("db.test.activity-callback-probe")]
public interface IActivityCallbackProbe : INeuron, IHandle<ActivityInput>, IHandle<ActivityChanged>;

internal sealed class ActivityCallbackProbe(NeuronRuntime runtime) : Neuron(runtime), IActivityCallbackProbe
{
    public async Task HandleAsync(ActivityInput signal, CancellationToken cancellationToken)
    {
        // The source's deferred callback waits on this busy neuron while it emits another fact.
        await Task.Delay(250, cancellationToken);
        await RecordOutgoingAsync(new ActivityInput("child observation")).ConfigureAwait(true);
    }
    public Task HandleAsync(ActivityChanged signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

[Alias("db.test.activity-retry-sink")]
public interface IActivityRetrySink : INeuron, IHandle<ActivityChanged>;

internal sealed class ActivityRetrySink : Neuron, IActivityRetrySink
{
    private readonly IDurableValue<bool> _failed;
    public ActivityRetrySink(NeuronRuntime runtime) : base(runtime)
        => _failed = ServiceProvider.GetRequiredKeyedService<IDurableValue<bool>>("failed-once");
    public async Task HandleAsync(ActivityChanged signal, CancellationToken cancellationToken)
    {
        if (!_failed.Value)
        {
            _failed.Value = true;
            await WriteStateAsync(cancellationToken);
            throw new InvalidOperationException("Subscriber temporarily unavailable.");
        }
    }
}

public sealed class ActivityRuntimeTests
{
    [Fact]
    public async Task QueuedFactsSurviveActivationCollectionBeforeTheCollectorIsWired()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var correlation = CorrelationId.New();
        var signalId = SignalId.New();
        var fact = new ActivityExecutionChanged(correlation, principal, "queued", signalId, null,
            IBrainNeuron.ForOwner(sim.Brain.Owner), null, nameof(ActivityInput), "observed",
            DateTimeOffset.UtcNow, "before startup wiring");
        await source.SendAsync(fact, token);
        await sim.Grains.GetGrain<Orleans.Runtime.IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        var delivered = await JournalWait.ForAsync(activities, JournalKind.Outgoing,
            item => item.Signal is ActivityChanged { Activity.Title: "before startup wiring" },
            TimeSpan.FromSeconds(15), cancellationToken: token);
        var view = Assert.IsType<ActivityChanged>(delivered.Signal).Activity;
        Assert.Equal(correlation.ToString(), view.CorrelationId);
        Assert.Equal(signalId.ToString(), view.RootSignalId);
        Assert.Equal(principal, view.Principal);
        Assert.Equal(principal, delivered.Principal);
        Assert.True(delivered.IsActivityTelemetry);
    }

    [Fact]
    public async Task ACommittedFactIsRepublishedAtTheSameVersionWhenItsObserverFails()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var sink = sim.Brain.Get<IActivityRetrySink>("single-retry");
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        await sink.SubscribeToAsync<IActivityRetrySink, IActivities, ActivityChanged>(activities.Id, token);
        await source.SendAsync(new ActivityExecutionChanged(CorrelationId.New(), null, "single", SignalId.New(), null,
            IBrainNeuron.ForOwner(sim.Brain.Owner), null, nameof(ActivityInput), "observed",
            DateTimeOffset.UtcNow, "single retry"), token);
        await JournalWait.ForAsync(sink, JournalKind.Incoming,
            item => item.Signal is ActivityChanged { Activity.Title: "single retry" },
            TimeSpan.FromSeconds(15), cancellationToken: token);
        var changes = (await activities.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: token)).Delta
            .Select(item => item.Signal).OfType<ActivityChanged>().ToArray();
        Assert.Equal(2, changes.Length);
        Assert.All(changes, change =>
        {
            Assert.Equal(1, change.Activity.Version);
            Assert.Single(change.Activity.Events);
        });
    }

    [Fact]
    public async Task DeferredCallbackCannotBlockItsBusyProducerFromReportingMoreFacts()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var probe = sim.Brain.Get<IActivityCallbackProbe>("busy");
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        await probe.SubscribeToAsync<IActivityCallbackProbe, IActivities, ActivityChanged>(activities.Id, token);
        await probe.SendAsync(new ActivityInput("self observer"), token).WaitAsync(TimeSpan.FromSeconds(8), token);
        await JournalWait.ForAsync(activities, JournalKind.Outgoing,
            item => item.Signal is ActivityChanged { Activity.Title: "self observer", Activity.Status: "completed" },
            TimeSpan.FromSeconds(15), cancellationToken: token);
    }

    [Fact]
    public async Task AFailingObserverIsRetriedWithoutFailingTheBusinessInput()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var sink = sim.Brain.Get<IActivityRetrySink>("retry");
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        await sink.SubscribeToAsync<IActivityRetrySink, IActivities, ActivityChanged>(activities.Id, token);
        await sim.Brain.Get<IActivityProbe>("one").SendAsync(new ActivityInput("observer retry"), token);
        await JournalWait.ForAsync(sink, JournalKind.Incoming,
            item => item.Signal is ActivityChanged { Activity.Title: "observer retry", Activity.Status: "completed" },
            TimeSpan.FromSeconds(15), cancellationToken: token);
    }

    [Fact]
    public async Task RootBroadcastSettlesOnlyAfterAllRecipientsAndRecordsEmptyAudience()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        var root = IBrainNeuron.ForOwner(sim.Brain.Owner);
        foreach (var name in new[] { "first", "second" })
        {
            await sim.Grains.GetGrain<IActivityProbe>(sim.Brain.Get<IActivityProbe>(name).Id.ToGrainId())
                .HandleAsync(new Subscribe(root, nameof(ActivityInput)), token);
        }
        await sim.Grains.GetGrain<INeuronGrain>(root.ToGrainId()).Broadcast(new ActivityInput("fanout"), token);
        await WaitFor(activities, "fanout", "completed", token);
        var updates = (await activities.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: token))
            .Delta.Select(delivery => delivery.Signal).OfType<ActivityChanged>()
            .Where(change => change.Activity.Title == "fanout").ToArray();
        Assert.Single(updates, change => change.Activity.Status == "completed");
        var completed = updates[^1].Activity;
        Assert.Contains("activityprobe:first", completed.ParticipantNeuronIds);
        Assert.Contains("activityprobe:second", completed.ParticipantNeuronIds);
    }

    [Fact]
    public async Task AnUndeliveredJournalFactIsObservedRatherThanCompleted()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        var id = new NeuronId("journalprobe", sim.Brain.Owner, "observed");
        await sim.Grains.GetGrain<IJournalProbe>(id.ToGrainId()).Record("timer observation");
        await JournalWait.ForAsync(activities, JournalKind.Outgoing,
            item => item.Signal is ActivityChanged { Activity.Status: "observed" },
            TimeSpan.FromSeconds(15), cancellationToken: token);
        var snapshot = await activities.RequestAsync(new ReadActivities(), token);
        Assert.Equal("observed", Assert.Single(snapshot.Activities).Status);
    }

    [Fact]
    public async Task RuntimeFactsTravelThroughSubscribedSourceAndDurableActivityNeuron()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        await sim.Brain.Get<IActivityProbe>("one").SendAsync(new ActivityInput("first"), token);
        await sim.Brain.Get<IActivityProbe>("one").SendAsync(new ActivityInput("second"), token);
        await WaitFor(activities, "first", "completed", token);
        await WaitFor(activities, "second", "completed", token);

        var snapshot = await activities.RequestAsync(new ReadActivities(), token);
        Assert.Equal(2, snapshot.Activities.Length);
        Assert.All(snapshot.Activities, item => Assert.Equal("completed", item.Status));
        Assert.Equal(2, snapshot.Activities.Select(item => item.Id).Distinct().Count());
        Assert.Contains(snapshot.Activities, item => item.Title == "first");
        Assert.Contains((await activities.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: token)).Delta,
            delivery => delivery.Signal is ActivityChanged);
    }

    [Fact]
    public async Task ASubscriberReactionDoesNotCreateTelemetryActivities()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var echo = sim.Brain.Get<IActivityEcho>("echo");
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        await echo.SubscribeToAsync<IActivityEcho, IActivities, ActivityChanged>(activities.Id, token);
        await sim.Brain.Get<IActivityProbe>("one").SendAsync(new ActivityInput("real work"), token);
        await WaitFor(activities, "real work", "completed", token);
        await JournalWait.ForAsync(echo, JournalKind.Outgoing,
            item => item.Signal is ActivityInput, TimeSpan.FromSeconds(15), cancellationToken: token);

        var snapshot = await activities.RequestAsync(new ReadActivities(), token);
        Assert.Single(snapshot.Activities);
        Assert.Equal("real work", snapshot.Activities[0].Title);
        var output = await echo.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: token);
        Assert.NotEmpty(output.Delta);
        Assert.All(output.Delta, delivery => Assert.True(delivery.IsActivityTelemetry));
    }

    [Fact]
    public async Task ActivityReadsDoNotRevealAnotherPrincipalsWork()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        var first = PrincipalId.New();
        var second = PrincipalId.New();
        using (VerifiedActor.Enter(new ActorContext(first, "first")))
        {
            await sim.Brain.Get<IActivityProbe>("one").SendAsync(new ActivityInput("private first"), token);
            await WaitFor(activities, "private first", "completed", token);
        }
        using (VerifiedActor.Enter(new ActorContext(second, "second")))
        {
            await sim.Brain.Get<IActivityProbe>("one").SendAsync(new ActivityInput("private second"), token);
            await WaitFor(activities, "private second", "completed", token);
            var snapshot = await activities.RequestAsync(new ReadActivities(), token);
            Assert.Equal("private second", Assert.Single(snapshot.Activities).Title);
        }
    }

    private static async Task WaitFor(NeuronReference<IActivities> activities, string title, string status, CancellationToken token)
        => await JournalWait.ForAsync(activities, JournalKind.Outgoing,
            item => item.Signal is ActivityChanged change && change.Activity.Title == title && change.Activity.Status == status,
            TimeSpan.FromSeconds(15), cancellationToken: token);
}
