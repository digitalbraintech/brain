using System.Collections.Concurrent;
using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AgentDelegationTests
{
    [Fact]
    public async Task ChatDelegatesCommonAgentRequestsWithLiveEvidenceAndIsolatedPrincipalTargets()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var cancellationToken = deadline.Token;
        var client = new DelegationChatClient(pauseFirstProbe: true);
        var delegation = new CapturingDelegation();
        await using var simulation = await StartAsync(client, delegation);
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "first");
        var other = new ActorContext(new PrincipalId(Guid.NewGuid()), "second");
        var assistant = simulation.Brain.Get<IAssistant>("assistant").Id;
        var target = Target(assistant.Owner, actor.PrincipalId);
        var firstCommand = CommandId.New();
        var firstChat = simulation.Brain.Get<IChat>(PrincipalPartition.InstanceName(actor.PrincipalId, "main"));

        try
        {
            var accepted = await firstChat.RequestAsync(new SendMessage(firstCommand, "check status", actor), cancellationToken);
            await client.ProbeStarted.Task.WaitAsync(cancellationToken);

            // A first delegation is visible while the specialist is running, before
            // any learned route exists and before its reply can enter busy Ino.
            var inflight = await Query(simulation, assistant).ReadJournal(JournalKind.Outgoing, 0);
            Assert.Contains(inflight.Delta, delivery => delivery.Signal is AgentActivity
                { Kind: "delegation", State: "started" } activity && activity.Target == target);
            Assert.DoesNotContain(await Query(simulation, assistant).ReadSynapses(), route => route.Target == target);
            Assert.Contains((await Query(simulation, target).ReadJournal(JournalKind.Outgoing, 0)).Delta,
                delivery => delivery.Signal is AgentActivity { Kind: "agent", State: "started" });

            client.ContinueProbe.TrySetResult();
            var first = await AwaitReplyAsync(firstChat, accepted.TurnId, firstCommand, cancellationToken);
            var repeated = await SendAsync(actor);
            var secondPrincipal = await SendAsync(other);

            Assert.Equal(Expected(actor.PrincipalId, 1, 2), first);
            Assert.Equal(Expected(actor.PrincipalId, 3, 4), repeated);
            Assert.Equal(Expected(other.PrincipalId, 1, 2), secondPrincipal);
            Assert.NotEqual(first, repeated);
            var conversationSpans = spans.Where(span => Equals(
                span.GetTagItem("gen_ai.conversation.id"), firstChat.Id.ToString())).ToArray();
            Assert.Equal(6, conversationSpans.Length); // Two Ino turns, each with two delegates.
            Assert.Contains(conversationSpans, span => span.DisplayName == "invoke_agent Ino");
            Assert.Equal(4, conversationSpans.Count(span => span.DisplayName == "invoke_agent Probe"));
            Assert.All(conversationSpans, span =>
            {
                Assert.NotNull(span.GetTagItem("gen_ai.agent.id"));
                Assert.NotNull(span.GetTagItem("db.command.id"));
                Assert.Equal("completed", span.GetTagItem("db.agent.state"));
            });
            Assert.All(client.AssistantTools, names =>
            {
                Assert.Contains("ask_probe", names);
                Assert.DoesNotContain("probe_read", names);
            });
            Assert.Equal(6, client.ProbeCalls.Count);
            Assert.All(client.ProbeCalls, call =>
            {
                Assert.Equal("check status", call.Request);
                Assert.Equal(["probe_read"], call.ToolNames);
                Assert.NotNull(call.Command);
                Assert.True(call.Chat is { } chat && PrincipalPartition.OwnsInstance(call.Principal, chat.Name));
            });

            var journal = await Query(simulation, assistant).ReadJournal(JournalKind.Outgoing, 0);
            Assert.Equal(6, journal.Delta.Count(delivery => delivery.Signal is AgentRequest));
            var activities = journal.Delta.Select(delivery => delivery.Signal).OfType<AgentActivity>()
                .Where(activity => activity.Kind == "delegation").ToArray();
            Assert.Equal(12, activities.Length);
            foreach (var operation in activities.GroupBy(activity => activity.OperationId))
            {
                Assert.Single(operation, activity => activity.State == "started");
                var completed = Assert.Single(operation, activity => activity.State == "completed");
                Assert.True(completed.DurationMs >= 0);
            }

            var routes = await Query(simulation, assistant).ReadSynapses();
            foreach (var (principal, count) in new[] { (actor.PrincipalId, 4), (other.PrincipalId, 2) })
            {
                var principalTarget = Target(assistant.Owner, principal);
                var route = Assert.Single(routes, candidate => candidate.Target == principalTarget);
                Assert.Equal(SynapseKind.Learned, route.Kind);
                Assert.Equal(nameof(AgentRequest), route.SignalType);
                Assert.Equal(count, route.FireCount);

                var incoming = (await Query(simulation, principalTarget).ReadJournal(JournalKind.Incoming, 0)).Delta;
                var requests = incoming.Where(delivery => delivery.Signal is AgentRequest).ToArray();
                Assert.Equal(count, requests.Length);
                Assert.All(requests, request =>
                {
                    Assert.Equal(principal, request.Principal);
                    Assert.Equal(assistant, request.Caller);
                });
                var replies = (await Query(simulation, principalTarget).ReadJournal(JournalKind.Outgoing, 0))
                    .Delta.Where(delivery => delivery.Signal is AgentReply).ToArray();
                foreach (var request in requests)
                {
                    var reply = Assert.Single(replies, candidate => candidate.CausationId == request.SignalId);
                    Assert.Equal(principal, reply.Principal);
                    Assert.StartsWith($"probe:{principal.Value:N}:", ((AgentReply)reply.Signal).Text, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            client.ContinueProbe.TrySetResult();
        }

        async Task<string> SendAsync(ActorContext sender)
        {
            var chat = simulation.Brain.Get<IChat>(PrincipalPartition.InstanceName(sender.PrincipalId, "main"));
            var command = CommandId.New();
            var accepted = await chat.RequestAsync(new SendMessage(command, "check status", sender), cancellationToken);
            return await AwaitReplyAsync(chat, accepted.TurnId, command, cancellationToken);
        }
    }

    [Fact]
    public async Task RestrictedContinuationCannotTurnDelegationNameIntoASpecialistToolGrant()
    {
        var client = new DelegationChatClient();
        var delegation = new CapturingDelegation();
        await using var simulation = await StartAsync(client, delegation);
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        var assistant = simulation.Brain.Get<IAssistant>("assistant").Id;
        var chat = new NeuronId("chat", assistant.Owner, PrincipalPartition.InstanceName(actor.PrincipalId, "main"));
        using var verified = VerifiedActor.Enter(actor);
        using var turn = AgentTurnContext.Enter(new AgentTurnContext(chat, CommandId.New(), actor, ["ask_probe"]));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            simulation.Grains.GetGrain<IAgentKernel>(assistant.ToGrainId())
                .Ask(new AgentRequest("check status"), CorrelationId.New(), TestContext.Current.CancellationToken));

        Assert.Contains("restricted", failure.Message, StringComparison.Ordinal);
        Assert.Empty(client.ProbeCalls);
        Assert.Equal(["ask_probe"], Assert.Single(client.AssistantTools));
        Assert.Empty(await Query(simulation, assistant).ReadSynapses());
        Assert.DoesNotContain((await Query(simulation, assistant).ReadJournal(JournalKind.Outgoing, 0)).Delta,
            delivery => delivery.Signal is AgentRequest);
    }

    [Fact]
    public async Task CapturedRequestCapabilityExpiresWhenItsModelTurnEnds()
    {
        var client = new DelegationChatClient();
        var delegation = new CapturingDelegation();
        await using var simulation = await StartAsync(client, delegation);
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        var assistant = simulation.Brain.Get<IAssistant>("assistant").Id;
        using var verified = VerifiedActor.Enter(actor);
        await simulation.Grains.GetGrain<IAgentKernel>(assistant.ToGrainId())
            .Ask(new AgentRequest("check status"), CorrelationId.New(), TestContext.Current.CancellationToken);
        var context = Assert.Single(delegation.Contexts);
        var before = await Query(simulation, assistant).ReadJournal(JournalKind.Outgoing, 0);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Requests.RequestAsync<IProbe>(
            Target(assistant.Owner, actor.PrincipalId).Name, new AgentRequest("late request"), TestContext.Current.CancellationToken));

        Assert.Contains("expired", failure.Message, StringComparison.Ordinal);
        var after = await Query(simulation, assistant).ReadJournal(JournalKind.Outgoing, before.ResumeSequence);
        Assert.Empty(after.Delta);
        Assert.Equal(2, client.ProbeCalls.Count);
    }

    private static Task<BrainSimulation> StartAsync(IChatClient client, CapturingDelegation delegation)
        => BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(DigitalBrain.Execution.ExecutionModule), typeof(DigitalBrain.UI.UIModule), typeof(AIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<IAgentToolSource>(delegation);
                silo.Services.AddSingleton(client);
            },
        });

    private static async Task<string> AwaitReplyAsync(
        NeuronReference<IChat> chat, TurnId turnId, CommandId command, CancellationToken cancellationToken)
    {
        var terminal = await JournalWait.ForAsync(chat, JournalKind.Outgoing,
            delivery => delivery.Signal is TurnLifecycle life && life.TurnId == turnId
                && life.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled,
            TimeSpan.FromSeconds(25), cancellationToken: cancellationToken);
        var lifecycle = Assert.IsType<TurnLifecycle>(terminal.Signal);
        Assert.True(lifecycle.Status == ChatTurnStatus.Completed, lifecycle.Detail);
        var response = await JournalWait.ForAsync(chat, JournalKind.Outgoing,
            delivery => delivery.Signal is Responded reply && reply.CommandId == command && reply.TurnId == turnId,
            TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
        return Assert.IsType<Responded>(response.Signal).Text;
    }

    private static INeuronQuery Query(BrainSimulation simulation, NeuronId neuron)
        => simulation.Grains.GetGrain<INeuronQuery>(neuron.ToGrainId());

    private static NeuronId Target(OwnerId owner, PrincipalId principal)
        => NeuronId.For<IProbe>(owner, PrincipalPartition.InstanceName(principal, "application"));

    private static string Expected(PrincipalId principal, int first, int second)
        => $"{DelegationChatClient.Reply(principal, first)} | {DelegationChatClient.Reply(principal, second)}";
}
