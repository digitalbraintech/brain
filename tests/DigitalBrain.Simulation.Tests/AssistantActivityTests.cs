using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AssistantActivityTests
{
    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task Deferred_assistant_work_keeps_its_activity_open_until_the_worker_settles(string terminal)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var model = new PausedChatClient(terminal);
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(UIModule), typeof(AIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model),
        });
        var actor = new ActorContext(PrincipalId.New(), "owner");
        using var verified = VerifiedActor.Enter(actor);
        var activities = sim.Brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var source = sim.Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var inbox = sim.Brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var assistant = sim.Brain.Get<IAssistant>("assistant");
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(source.Id, token);
        await assistant.SubscribeToAsync<IAssistant, IComposer, UserMessaged>(inbox.Id, token);

        try
        {
            var command = await new WorkspaceInject(sim.Brain, sim.Grains)
                .InjectUserMessage("main", "Hold this request open", actor, token);
            await model.Started.Task.WaitAsync(token);
            var input = await JournalWait.ForAsync(assistant, JournalKind.Incoming,
                delivery => delivery.Signal is UserMessaged message && message.CommandId == command,
                cancellationToken: token);
            var operation = $"assistant-turn:{input.CorrelationId}";
            await JournalWait.ForAsync(activities, JournalKind.Outgoing,
                delivery => delivery.Signal is ActivityChanged changed
                    && changed.Activity.CommandId == command.ToString()
                    && changed.Activity.Events.LastOrDefault(item => item.OperationId == operation)?.Phase == "running",
                cancellationToken: token);
            var snapshot = await activities.RequestAsync(new ReadActivities(), token);
            var running = Assert.Single(snapshot.Activities, activity => activity.CommandId == command.ToString());
            Assert.Equal("running", running.Status);
            Assert.Equal(actor.PrincipalId, running.Principal);
            var execution = running.Events.Where(item => item.OperationId == operation).ToArray();
            Assert.Equal<string>(["waiting", "running"], execution.Select(item => item.Phase));
            Assert.All(execution, item =>
            {
                Assert.Equal(input.SignalId.ToString(), item.SignalId);
                Assert.Equal(input.CausationId?.ToString(), item.CausationId);
                Assert.Equal($"{input.Caller.Type}:{input.Caller.Name}", item.SourceNeuronId);
                Assert.Equal("assistant:assistant", item.TargetNeuronId);
            });

            model.Release.TrySetResult();
            var settledDelivery = await JournalWait.ForAsync(activities, JournalKind.Outgoing,
                delivery => delivery.Signal is ActivityChanged changed
                    && changed.Activity.CommandId == command.ToString()
                    && changed.Activity.Events.LastOrDefault(item => item.OperationId == operation)?.Phase == terminal,
                cancellationToken: token);
            var settled = Assert.IsType<ActivityChanged>(settledDelivery.Signal).Activity;
            Assert.Equal(terminal, settled.Status);
            Assert.Equal(actor.PrincipalId, settled.Principal);
            if (terminal == "completed")
            {
                await JournalWait.ForAsync(assistant, JournalKind.Outgoing,
                    delivery => delivery.Signal is Responded reply && reply.CommandId == command && reply.Text == "Finished",
                    cancellationToken: token);
            }
        }
        finally
        {
            model.Release.TrySetResult();
        }
    }

    private sealed class PausedChatClient(string terminal) : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (terminal == "failed")
            {
                throw new InvalidOperationException("Model failed.");
            }
            if (terminal == "cancelled")
            {
                throw new OperationCanceledException("Model cancelled.");
            }
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Finished") { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
