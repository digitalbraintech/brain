using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Startup;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class BehaviorExecutionIntegrationTests
{
    [Fact]
    public async Task Ordinary_program_calls_real_agents_in_parallel_and_publishes_one_replay_safe_note()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var model = new ParallelModel();
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(DigitalBrain.UI.UIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model),
        });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "owner"));
        var behavior = sim.Brain.Get<IBehavior>("parallel-review");
        var chat = sim.Brain.Get<IChat>("main");
        var kernel = sim.Grains.GetGrain<IBehaviorKernel>(behavior.Id.ToGrainId());
        var source = """
            await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
            var engineer = digitalBrain.Get<IAssistant>("engineer").RequestAsync(new AgentRequest("engineering"), CancellationToken);
            var architect = digitalBrain.Get<IAssistant>("architect").RequestAsync(new AgentRequest("architecture"), CancellationToken);
            var replies = await Task.WhenAll(engineer, architect);
            return new Note(string.Join(" | ", replies.Select(reply => reply.Text)));
            """;
        var saved = await behavior.RequestAsync(new SaveBehaviorScript(source, [nameof(NewPost)], [nameof(Note)]), token);
        var runner = new BehaviorProgramRunner();
        Assert.Empty(runner.Validate(saved.Behavior.Draft!));
        await kernel.ValidateDraft(saved.Behavior.Draft!.Revision, []);
        await chat.SendAsync(new Subscribe(behavior.Id, nameof(Note)), token);
        await behavior.RequestAsync(new EnableBehavior(saved.Behavior.Draft.Revision), token);
        using var worker = new BehaviorExecutionWorker(sim.Brain, sim.Grains, runner, NullLogger<BehaviorExecutionWorker>.Instance);
        await worker.StartAsync(token);
        try
        {
            await behavior.RequestAsync(new InvokeBehavior(new NewPost("review")), token);
            // Neither model request may finish before both separate target neurons enter.
            await model.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            model.Release.TrySetResult();
            SignalDelivery delivery;
            try
            {
                delivery = await JournalWait.ForAsync(behavior, JournalKind.Outgoing,
                    item => item.Signal is Note, TimeSpan.FromSeconds(20), cancellationToken: token);
            }
            catch (Exception exception)
            {
                var state = await kernel.ReadState();
                throw new InvalidOperationException($"Behavior output missing: enabled={state.Enabled}, pending={state.PendingCount}, detail={state.Detail}", exception);
            }
            await JournalWait.ForAsync(chat, JournalKind.Outgoing,
                item => item.Signal is Responded responded && responded.Text.Contains("engineering", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10), cancellationToken: token);
            var sink = sim.Grains.GetGrain<INeuronGrain>(chat.Id.ToGrainId());
            await sink.Deliver(delivery, token);
            await sink.Deliver(delivery, token);
            var transcript = await chat.RequestAsync(new ReadTranscriptRequest(chat.Id.Name), token);
            Assert.Single(transcript.Transcript.Turns, turn => turn.Text == "engineering | architecture");
            Assert.Equal(2, model.Entered);
            await behavior.RequestAsync(new DisableBehavior(), token);
            await sink.Deliver(SignalDelivery.Create(new Note("late old work"), delivery.Caller, delivery.Sequence + 1,
                TimeProvider.System, principal: principal, sourceEpoch: delivery.SourceEpoch), token);
            transcript = await chat.RequestAsync(new ReadTranscriptRequest(chat.Id.Name), token);
            Assert.DoesNotContain(transcript.Transcript.Turns, turn => turn.Text == "late old work");
        }
        finally
        {
            model.Release.TrySetResult();
            await worker.StopAsync(token);
        }
    }

    private sealed class ParallelModel : IChatClient
    {
        private int _entered;
        public int Entered => Volatile.Read(ref _entered);
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _entered) == 2)
            {
                BothEntered.TrySetResult();
            }

            await Release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, messages.Last(message => message.Role == ChatRole.User).Text)
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
