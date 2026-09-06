using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class BehaviorActivityTests
{
    [Theory]
    [InlineData(false, "completed")]
    [InlineData(true, "cancelled")]
    public async Task DurableWorkStaysOpenAfterInputAcceptanceAndSettlesExplicitly(bool disable, string finalStatus)
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var token = TestContext.Current.CancellationToken;
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        var behavior = sim.Brain.Get<IBehavior>(PrincipalPartition.InstanceName(principal, "activity"));
        var kernel = sim.Grains.GetGrain<IBehaviorKernel>(behavior.Id.ToGrainId());
        var saved = await behavior.RequestAsync(new SaveBehaviorScript("return null;", [nameof(NewPost)], []), token);
        await kernel.ValidateDraft(saved.Behavior.Draft!.Revision, []);
        await behavior.RequestAsync(new EnableBehavior(), token);
        await behavior.RequestAsync(new InvokeBehavior(new NewPost("work")), token);
        await WaitFor("waiting");
        var initial = Assert.Single((await activities.RequestAsync(new ReadActivities(), token)).Activities,
            item => item.TriggerName == nameof(InvokeBehavior));
        Assert.Equal("waiting", initial.Status);
        var claim = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        await WaitFor("running");
        Assert.Equal("running", (await Read()).Status);
        if (disable)
        {
            await behavior.RequestAsync(new DisableBehavior(), token);
        }
        else
        {
            await kernel.Complete(claim, null);
        }
        await WaitFor(finalStatus);
        var final = await Read();
        Assert.Equal(finalStatus, final.Status);
        Assert.Contains(final.Events, item => item.BehaviorRevision == claim.Program.Revision.ToString("N"));
        await sim.Grains.GetGrain<Orleans.Runtime.IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);
        Assert.Equal(finalStatus, (await Read()).Status);

        async Task<ActivityView> Read() => Assert.Single(
            (await activities.RequestAsync(new ReadActivities(), token)).Activities, item => item.Id == initial.Id);

        async Task WaitFor(string status)
            => await JournalWait.ForAsync(activities, JournalKind.Outgoing,
                item => item.Signal is ActivityChanged change
                    && change.Activity.TriggerName == nameof(InvokeBehavior) && change.Activity.Status == status
                    && change.Activity.Events.LastOrDefault(item => item.OperationId.StartsWith($"behavior:{behavior.Id}:", StringComparison.Ordinal))?.Phase == status,
                TimeSpan.FromSeconds(15), cancellationToken: token);
    }
}
