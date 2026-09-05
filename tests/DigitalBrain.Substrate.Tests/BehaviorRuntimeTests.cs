using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class BehaviorRuntimeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Invalid_edit_preserves_active_revision_and_draft_is_discoverable()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var behavior = sim.Brain.Get<IBehavior>(PrincipalPartition.InstanceName(principal, "review"));
        var kernel = sim.Grains.GetGrain<IBehaviorKernel>(behavior.Id.ToGrainId());
        var saved = await behavior.RequestAsync(new SaveBehaviorScript("return Input;", [nameof(NewPost)], [nameof(NewPost)]), Token);
        Assert.False(saved.Behavior.Enabled);
        await kernel.ValidateDraft(saved.Behavior.Draft!.Revision, []);
        await behavior.RequestAsync(new EnableBehavior(), Token);
        var active = (await kernel.ReadState()).Active;
        var edited = await behavior.RequestAsync(new SaveBehaviorScript("this is not C#", [nameof(NewPost)], [nameof(NewPost)]), Token);
        await kernel.ValidateDraft(edited.Behavior.Draft!.Revision, ["Compilation failed"]);
        var state = await kernel.ReadState();
        Assert.Equal(active!.Revision, state.Active!.Revision);
        Assert.True(state.Enabled);
        Assert.Equal(BehaviorValidation.Invalid, state.Draft!.Validation);
        var index = sim.Grains.GetGrain<IBehaviorsKernel>(NeuronId.For<IBehaviors>(sim.Brain.Owner, "default").ToGrainId());
        Assert.Contains(behavior.Id, await index.ReadBehaviorIds());
        await Assert.ThrowsAnyAsync<Exception>(() => behavior.RequestAsync(new EnableBehavior(), Token));
    }

    [Fact]
    public async Task Duplicate_input_and_reclaimed_request_keep_one_identity_and_completed_checkpoint()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]), ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(time) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "review", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        await behavior.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        var input = SignalDelivery.Create(new NewPost("one"), source.Id, 1, time, principal: principal);
        var receiver = sim.Grains.GetGrain<INeuronGrain>(behavior.Id.ToGrainId());
        await receiver.Deliver(input, Token);
        await receiver.Deliver(input, Token);
        Assert.Equal(1, (await kernel.ReadState()).PendingCount);
        var first = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        var prepared = await kernel.PrepareRequest(first, "one", "hash", source.Id, new PublishPost("request"));
        await kernel.CompleteRequest(first, "one", "hash");
        await kernel.CompleteRequest(first, "one", "hash");
        var learned = Assert.Single(await behavior.GetSynapsesAsync(Token), edge => edge.Target == source.Id
            && edge.Kind == SynapseKind.Learned && edge.SignalType == nameof(PublishPost));
        Assert.Equal(1, learned.FireCount);
        await kernel.StoreCheckpoint(first, new("one", "hash", new NewPost("checkpoint")));
        time.Advance(TimeSpan.FromMinutes(4));
        await sim.Grains.GetGrain<Orleans.Runtime.IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);
        var second = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal(first.WorkId, second.WorkId);
        Assert.False(await kernel.Renew(first));
        Assert.Equal(prepared.SignalId, (await kernel.PrepareRequest(second, "one", "hash", source.Id, new PublishPost("request"))).SignalId);
        await kernel.CompleteRequest(second, "one", "hash");
        Assert.Equal(1, Assert.Single(await behavior.GetSynapsesAsync(Token), edge => edge.Target == source.Id
            && edge.Kind == SynapseKind.Learned && edge.SignalType == nameof(PublishPost)).FireCount);
        Assert.Equal("checkpoint", Assert.IsType<NewPost>((await kernel.ReadCheckpoint(second, "one", "hash"))!.Response).Text);
        await kernel.Complete(second, null);
        Assert.Equal(0, (await kernel.ReadState()).PendingCount);
    }

    [Fact]
    public async Task Disable_removes_only_its_edges_fences_claim_and_enable_restores_subscription()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (first, firstKernel) = await Enabled(sim, principal, "one", nameof(NewPost));
        var (second, _) = await Enabled(sim, principal, "two", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        await first.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await second.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await first.RequestAsync(new InvokeBehavior(new NewPost("manual")), Token);
        var claim = Assert.IsType<BehaviorClaim>(await firstKernel.TryClaim());
        await first.RequestAsync(new DisableBehavior(), Token);
        Assert.False(await firstKernel.Renew(claim));
        var query = sim.Grains.GetGrain<INeuronQuery>(source.Id.ToGrainId());
        var edges = await query.ReadSynapses();
        Assert.DoesNotContain(edges, edge => edge.Target == first.Id && edge.Kind == SynapseKind.Bound);
        Assert.Contains(edges, edge => edge.Target == second.Id && edge.Kind == SynapseKind.Bound);
        await first.RequestAsync(new EnableBehavior(), Token);
        Assert.Contains(await query.ReadSynapses(), edge => edge.Target == first.Id && edge.Kind == SynapseKind.Bound);
    }

    [Fact]
    public async Task Changed_subject_fences_old_claim_but_null_output_allows_later_green_input()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "versions", nameof(VersionedBehaviorFact),
            BehaviorInputPolicy.LatestPerSubject | BehaviorInputPolicy.OncePerVersion);
        await behavior.RequestAsync(new InvokeBehavior(new VersionedBehaviorFact("pr-1", "red", "head")), Token);
        var waiting = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        await kernel.Complete(waiting, null);
        await behavior.RequestAsync(new InvokeBehavior(new VersionedBehaviorFact("pr-1", "green", "head")), Token);
        var green = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        await behavior.RequestAsync(new InvokeBehavior(new VersionedBehaviorFact("pr-1", "new-head", "head2")), Token);
        Assert.False(await kernel.Renew(green));
        await kernel.Complete(green, new VersionedBehaviorFact("pr-1", "old-output", "head"));
        var fresh = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        Assert.Equal("new-head", Assert.IsType<VersionedBehaviorFact>(fresh.Input.Signal).Version);
        Assert.NotEqual(green.StreamGeneration, fresh.StreamGeneration);
    }

    [Fact]
    public async Task Activating_an_edit_pins_existing_work_and_unrelated_subjects_can_run_concurrently()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "parallel", nameof(VersionedBehaviorFact), BehaviorInputPolicy.LatestPerSubject);
        await behavior.RequestAsync(new InvokeBehavior(new VersionedBehaviorFact("one", "head", "head")), Token);
        await behavior.RequestAsync(new InvokeBehavior(new VersionedBehaviorFact("two", "head", "head")), Token);
        var first = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        var second = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        Assert.NotEqual(first.SourceStream, second.SourceStream);
        Assert.Null(await kernel.TryClaim());
        var edited = await behavior.RequestAsync(new SaveBehaviorScript("return null;", [nameof(VersionedBehaviorFact)], []), Token);
        await kernel.ValidateDraft(edited.Behavior.Draft!.Revision, []);
        await Assert.ThrowsAnyAsync<Exception>(() => behavior.RequestAsync(new EnableBehavior(first.Program.Revision), Token));
        await behavior.RequestAsync(new EnableBehavior(edited.Behavior.Draft.Revision), Token);
        Assert.True(await kernel.Renew(first));
        Assert.True(await kernel.Renew(second));
        Assert.NotEqual(first.Program.Revision, (await kernel.ReadState()).Active!.Revision);
        await kernel.Complete(first, null);
        await kernel.Complete(second, null);
        await behavior.RequestAsync(new InvokeBehavior(new VersionedBehaviorFact("three", "head", "head")), Token);
        Assert.Equal(edited.Behavior.Draft.Revision, (await kernel.TryClaim())!.Program.Revision);
    }

    [Fact]
    public async Task Replayed_mutation_keeps_saved_revision_and_manual_input_identity()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var behavior = sim.Brain.Get<IBehavior>(PrincipalPartition.InstanceName(principal, "commands"));
        var kernel = sim.Grains.GetGrain<IBehaviorKernel>(behavior.Id.ToGrainId());
        var receiver = sim.Grains.GetGrain<INeuronGrain>(behavior.Id.ToGrainId());
        var caller = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "caller"));
        var save = SignalDelivery.Create(new SaveBehaviorScript("return Input;", [nameof(NewPost)], [nameof(NewPost)]), caller.Id, 1, TimeProvider.System, principal: principal);
        await receiver.Deliver(save, Token);
        var revision = (await kernel.ReadState()).Draft!.Revision;
        await receiver.Deliver(save, Token);
        Assert.Equal(revision, (await kernel.ReadState()).Draft!.Revision);
        await kernel.ValidateDraft(revision, []);
        await behavior.RequestAsync(new EnableBehavior(revision), Token);
        var invoke = SignalDelivery.Create(new InvokeBehavior(new NewPost("once")), caller.Id, 2, TimeProvider.System, principal: principal);
        await receiver.Deliver(invoke, Token);
        await receiver.Deliver(invoke, Token);
        Assert.Equal(1, (await kernel.ReadState()).PendingCount);
    }

    [Fact]
    public async Task Runtime_revalidation_failure_disables_the_active_revision_and_fences_accepted_work()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "runtime-change", nameof(NewPost));
        await behavior.RequestAsync(new InvokeBehavior(new NewPost("pending")), Token);
        var claim = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        var newerDraft = await behavior.RequestAsync(new SaveBehaviorScript("return null;", [nameof(NewPost)], []), Token);
        await kernel.ValidateDraft(claim.Program.Revision, ["Installed contract no longer contains the requested API."], runtimeFingerprint: "changed-runtime");
        var state = await kernel.ReadState();
        Assert.False(state.Enabled);
        Assert.Equal(BehaviorValidation.Invalid, state.Active!.Validation);
        Assert.Equal("changed-runtime", state.Active.RuntimeFingerprint);
        Assert.Equal(newerDraft.Behavior.Draft!.Revision, state.Draft!.Revision);
        Assert.NotNull(state.Draft.SourceHash);
        Assert.False(await kernel.Renew(claim));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Interrupted_subscription_ack_preserves_desired_graph_edits_across_reactivation(bool subscribe)
    {
        var failure = new LostSubscriptionReply();
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IIncomingGrainCallFilter>(failure),
        });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, _) = await Enabled(sim, principal, "interrupted-wiring", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        if (!subscribe)
        {
            await behavior.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        }
        failure.Arm(subscribe ? nameof(INeuronGrain.BindOutgoing) : nameof(INeuronGrain.UnbindOutgoing));
        await Assert.ThrowsAnyAsync<Exception>(() => subscribe
            ? behavior.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token)
            : behavior.SendAsync(new Unsubscribe(source.Id, nameof(NewPost)), Token));
        var sourceQuery = sim.Grains.GetGrain<INeuronQuery>(source.Id.ToGrainId());
        Assert.Equal(subscribe, (await sourceQuery.ReadSynapses()).Any(edge => edge.Target == behavior.Id && edge.Kind == SynapseKind.Bound));
        await sim.Grains.GetGrain<Orleans.Runtime.IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);
        await behavior.RequestAsync(new DisableBehavior(), Token);
        Assert.DoesNotContain(await sourceQuery.ReadSynapses(), edge => edge.Target == behavior.Id && edge.Kind == SynapseKind.Bound);
        await behavior.RequestAsync(new EnableBehavior(), Token);
        Assert.Equal(subscribe, (await sourceQuery.ReadSynapses()).Any(edge => edge.Target == behavior.Id && edge.Kind == SynapseKind.Bound));
    }

    private sealed class LostSubscriptionReply : IIncomingGrainCallFilter
    {
        private string? _method;
        public void Arm(string method) => _method = method;
        public async Task Invoke(IIncomingGrainCallContext context)
        {
            var fail = context.InterfaceMethod?.DeclaringType == typeof(INeuronGrain) && context.MethodName == _method;
            if (fail)
            {
                _method = null;
            }
            await context.Invoke();
            if (fail)
            {
                throw new IOException("Injected loss of the acknowledgement after the source persisted its edge.");
            }
        }
    }

    [Fact]
    public async Task Source_revocation_fences_already_claimed_requests_and_preserves_other_sources()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "authority", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        var other = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "other"));
        var sink = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "sink"));
        await behavior.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await behavior.SendAsync(new Subscribe(other.Id, nameof(NewPost)), Token);
        var receiver = sim.Grains.GetGrain<INeuronGrain>(behavior.Id.ToGrainId());
        await receiver.Deliver(SignalDelivery.Create(new NewPost("one"), source.Id, 1, TimeProvider.System,
            principal: principal, sourceEpoch: 1), Token);
        await receiver.Deliver(SignalDelivery.Create(new NewPost("two"), other.Id, 1, TimeProvider.System,
            principal: principal, sourceEpoch: 1), Token);
        var first = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        var second = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        var request = await kernel.PrepareRequest(first, "pending", "hash", sink.Id, new PublishPost("stale effect"));
        await receiver.FenceSourceEpoch(source.Id, 2);
        Assert.False(await kernel.Renew(first));
        Assert.True(await kernel.Renew(second));
        await sim.Grains.GetGrain<INeuronGrain>(sink.Id.ToGrainId()).Deliver(request, Token);
        Assert.DoesNotContain((await sink.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: Token)).Delta,
            delivery => delivery.Signal is NewPost);
    }

    [Fact]
    public async Task Source_revocation_acknowledgement_fences_an_output_already_in_transit()
    {
        var blocked = new DelayedBehaviorPublication();
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IOutgoingGrainCallFilter>(blocked),
        });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "authority-output", nameof(NewPost), output: nameof(PublishPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        var sink = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "sink"));
        await behavior.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await sink.SendAsync(new Subscribe(behavior.Id, nameof(PublishPost)), Token);
        var receiver = sim.Grains.GetGrain<INeuronGrain>(behavior.Id.ToGrainId());
        await receiver.Deliver(SignalDelivery.Create(new NewPost("one"), source.Id, 1, TimeProvider.System,
            principal: principal, sourceEpoch: 1), Token);
        var claim = Assert.IsType<BehaviorClaim>(await kernel.TryClaim());
        try
        {
            await kernel.Complete(claim, new PublishPost("must not arrive after revocation"));
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            await receiver.FenceSourceEpoch(source.Id, 2);
            blocked.Release.TrySetResult();
            await blocked.Completed.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.DoesNotContain((await sink.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: Token)).Delta,
                delivery => delivery.Signal is NewPost);
        }
        finally
        {
            blocked.Release.TrySetResult();
        }
    }

    private sealed class DelayedBehaviorPublication : IOutgoingGrainCallFilter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Invoke(IOutgoingGrainCallContext context)
        {
            var delay = context.MethodName == nameof(INeuronGrain.Deliver) && context.Request.GetArgumentCount() > 0
                && context.Request.GetArgument(0) is SignalDelivery { Signal: PublishPost, Caller.Type: "behavior" };
            if (delay)
            {
                Started.TrySetResult();
                await Release.Task;
            }
            await context.Invoke();
            if (delay)
            {
                Completed.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task Source_revocation_propagates_through_a_completed_behavior_stage_to_an_in_transit_output()
    {
        var blocked = new DelayedBehaviorPublication();
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IOutgoingGrainCallFilter>(blocked),
        });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (first, firstKernel) = await Enabled(sim, principal, "first-stage", nameof(NewPost));
        var (second, secondKernel) = await Enabled(sim, principal, "second-stage", nameof(NewPost), output: nameof(PublishPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        var sink = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "sink"));
        await first.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await second.SendAsync(new Subscribe(first.Id, nameof(NewPost)), Token);
        await sink.SendAsync(new Subscribe(second.Id, nameof(PublishPost)), Token);
        var firstReceiver = sim.Grains.GetGrain<INeuronGrain>(first.Id.ToGrainId());
        await firstReceiver.Deliver(SignalDelivery.Create(new NewPost("input"), source.Id, 1, TimeProvider.System,
            principal: principal, sourceEpoch: 1), Token);
        await firstKernel.Complete(Assert.IsType<BehaviorClaim>(await firstKernel.TryClaim()), new NewPost("intermediate"));
        var secondClaim = await ClaimEventually(secondKernel);
        await WaitUntil(async () => (await firstKernel.ReadState()).Detail == "Output delivered.");
        try
        {
            await secondKernel.Complete(secondClaim, new PublishPost("late chained effect"));
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            await firstReceiver.FenceSourceEpoch(source.Id, 2).WaitAsync(TimeSpan.FromSeconds(10), Token);
            blocked.Release.TrySetResult();
            await blocked.Completed.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.DoesNotContain((await sink.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: Token)).Delta,
                delivery => delivery.Signal is NewPost);
        }
        finally
        {
            blocked.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Source_fencing_finishes_self_subscriptions_and_behavior_cycles_without_reopening_normal_turns()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (first, firstKernel) = await Enabled(sim, principal, "cycle-one", nameof(NewPost));
        var (second, secondKernel) = await Enabled(sim, principal, "cycle-two", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        await first.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await first.SendAsync(new Subscribe(first.Id, nameof(NewPost)), Token);
        await second.SendAsync(new Subscribe(first.Id, nameof(NewPost)), Token);
        await first.SendAsync(new Subscribe(second.Id, nameof(NewPost)), Token);
        var receiver = sim.Grains.GetGrain<INeuronGrain>(first.Id.ToGrainId());
        await receiver.Deliver(SignalDelivery.Create(new NewPost("input"), source.Id, 1, TimeProvider.System,
            principal: principal, sourceEpoch: 1), Token);
        await firstKernel.Complete(Assert.IsType<BehaviorClaim>(await firstKernel.TryClaim()), new NewPost("first output"));
        await secondKernel.Complete(await ClaimEventually(secondKernel), new NewPost("second output"));
        await WaitUntil(async () => (await firstKernel.ReadState()).PendingCount == 1);
        await receiver.FenceSourceEpoch(source.Id, 2).WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(0, (await firstKernel.ReadState()).PendingCount);
        Assert.Equal(0, (await secondKernel.ReadState()).PendingCount);
        await receiver.FenceSourceEpoch(source.Id, 2).WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True((await firstKernel.ReadState()).Enabled);
        Assert.True((await secondKernel.ReadState()).Enabled);
    }

    [Fact]
    public async Task Activation_rejects_removed_input_types_until_their_connections_are_removed()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (behavior, kernel) = await Enabled(sim, principal, "changed-types", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        await behavior.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        var oldRevision = (await kernel.ReadState()).Active!.Revision;
        var edit = await behavior.RequestAsync(new SaveBehaviorScript("return Input;", [nameof(PublishPost)], [nameof(PublishPost)]), Token);
        await kernel.ValidateDraft(edit.Behavior.Draft!.Revision, []);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => behavior.RequestAsync(new EnableBehavior(), Token));
        Assert.Contains("Unsubscribe NewPost", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(oldRevision, (await kernel.ReadState()).Active!.Revision);
        await behavior.SendAsync(new Unsubscribe(source.Id, nameof(NewPost)), Token);
        await behavior.RequestAsync(new EnableBehavior(), Token);
        Assert.Equal(edit.Behavior.Draft.Revision, (await kernel.ReadState()).Active!.Revision);
    }

    [Fact]
    public async Task Independent_source_fences_finish_when_two_behavior_roots_form_a_cycle()
    {
        var barrier = new ConcurrentFenceBarrier();
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IOutgoingGrainCallFilter>(barrier),
        });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var (first, firstKernel) = await Enabled(sim, principal, "concurrent-one", nameof(NewPost));
        var (second, secondKernel) = await Enabled(sim, principal, "concurrent-two", nameof(NewPost));
        var source = sim.Brain.Get<IXAccount>(PrincipalPartition.InstanceName(principal, "source"));
        await first.SendAsync(new Subscribe(source.Id, nameof(NewPost)), Token);
        await second.SendAsync(new Subscribe(first.Id, nameof(NewPost)), Token);
        await first.SendAsync(new Subscribe(second.Id, nameof(NewPost)), Token);
        var firstReceiver = sim.Grains.GetGrain<INeuronGrain>(first.Id.ToGrainId());
        var secondReceiver = sim.Grains.GetGrain<INeuronGrain>(second.Id.ToGrainId());
        await firstReceiver.Deliver(SignalDelivery.Create(new NewPost("input"), source.Id, 1, TimeProvider.System,
            principal: principal, sourceEpoch: 1), Token);
        await firstKernel.Complete(Assert.IsType<BehaviorClaim>(await firstKernel.TryClaim()), new NewPost("first output"));
        await secondKernel.Complete(await ClaimEventually(secondKernel), new NewPost("second output"));
        await WaitUntil(async () => (await firstKernel.ReadState()).PendingCount == 1);
        barrier.Armed = true;
        try
        {
            var firstFence = firstReceiver.FenceSourceEpoch(source.Id, 2);
            var secondFence = secondReceiver.FenceSourceEpoch(first.Id, 1);
            await barrier.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            barrier.Release.TrySetResult();
            await Task.WhenAll(firstFence, secondFence).WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(0, (await firstKernel.ReadState()).PendingCount);
            Assert.Equal(0, (await secondKernel.ReadState()).PendingCount);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
    }

    private sealed class ConcurrentFenceBarrier : IOutgoingGrainCallFilter
    {
        private int _started;
        public bool Armed { get; set; }
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Invoke(IOutgoingGrainCallContext context)
        {
            if (Armed && context.MethodName == nameof(INeuronGrain.FenceSourceStream))
            {
                if (Interlocked.Increment(ref _started) >= 2)
                {
                    BothStarted.TrySetResult();
                }
                await Release.Task;
            }
            await context.Invoke();
        }
    }

    private static async Task<BehaviorClaim> ClaimEventually(IBehaviorKernel kernel)
    {
        BehaviorClaim? claim = null;
        await WaitUntil(async () => (claim = await kernel.TryClaim()) is not null);
        return claim!;
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await condition())
        {
            await Task.Delay(25, deadline.Token);
        }
    }

    private static async Task<(NeuronReference<IBehavior> Reference, IBehaviorKernel Kernel)> Enabled(
        BrainSimulation sim, PrincipalId principal, string name, string input, BehaviorInputPolicy policy = BehaviorInputPolicy.EveryEvent, string? output = null)
    {
        var reference = sim.Brain.Get<IBehavior>(PrincipalPartition.InstanceName(principal, name));
        var kernel = sim.Grains.GetGrain<IBehaviorKernel>(reference.Id.ToGrainId());
        var saved = await reference.RequestAsync(new SaveBehaviorScript("return Input;", [input], [output ?? input], InputPolicy: policy), Token);
        await kernel.ValidateDraft(saved.Behavior.Draft!.Revision, []);
        await reference.RequestAsync(new EnableBehavior(), Token);
        return (reference, kernel);
    }
}

[GenerateSerializer]
public sealed record VersionedBehaviorFact([property: Id(0)] string SubjectKey, [property: Id(1)] string Version,
    [property: Id(2)] string CompletionKey) : Signal, IVersionedSignal
{
    public DateTimeOffset? CreatedAt => null;
}
