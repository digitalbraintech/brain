using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Scripting.Startup;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AssistantBehaviorTests
{
    [Fact]
    public async Task Chat_saves_inspects_activates_reuses_and_disables_a_personal_behavior()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var actor = new ActorContext(PrincipalId.New(), "owner");
        const string source = """
            await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
            var input = digitalBrain.Input<Note>();
            var reviewer = digitalBrain.Get<IAgent>("code-reviewer");
            var result = await reviewer.RequestAsync(new AgentRequest("read_repository_diff: " + input.Text), CancellationToken);
            return new Note(result.Text);
            """;
        var model = new BehaviorChatClient(source);
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(DigitalBrain.Execution.ExecutionModule), typeof(DigitalBrain.UI.UIModule), typeof(AIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model),
        });
        using var worker = new BehaviorExecutionWorker(sim.Brain, sim.Grains, new BehaviorProgramRunner(), NullLogger<BehaviorExecutionWorker>.Instance);
        await worker.StartAsync(token);
        try
        {
            var name = PrincipalPartition.InstanceName(actor.PrincipalId, "personal-review");
            var behavior = sim.Grains.GetGrain<IBehaviorKernel>(NeuronId.For<IBehavior>(sim.Brain.Owner, name).ToGrainId());
            await Send("save personal review");
            BehaviorView saved;
            using (VerifiedActor.Enter(actor))
            {
                do
                {
                    saved = await behavior.ReadState();
                    if (saved.Draft?.Validation != BehaviorValidation.Pending)
                    {
                        break;
                    }
                    await Task.Delay(25, token);
                } while (true);
            }
            Assert.NotNull(saved.Draft);
            Assert.Equal(BehaviorValidation.Valid, saved.Draft.Validation);
            Assert.Equal(source, saved.Draft.Source);
            Assert.False(saved.Enabled);
            var revision = saved.Draft.Revision;
            await Send("inspect personal review");
            Assert.Contains(model.Results, result => result.Contains("code-reviewer", StringComparison.Ordinal));
            await Send("publish personal review");
            await Send("activate personal review");
            await Send("invoke personal review");
            await model.ReviewerStarted.Task.WaitAsync(token);
            await Send("hello while reviewing").WaitAsync(TimeSpan.FromSeconds(5), token);
            model.ContinueReview.TrySetResult();
            var chat = sim.Brain.Get<IChat>(PrincipalPartition.InstanceName(actor.PrincipalId, "main"));
            await JournalWait.ForAsync(chat, JournalKind.Outgoing,
                delivery => delivery.Signal is Responded responded && responded.Text == BehaviorChatClient.Review,
                TimeSpan.FromSeconds(15), cancellationToken: token);
            await Send("invoke personal review");
            while ((await chat.RequestAsync(new ReadTranscriptRequest(chat.Id.Name), token)).Transcript.Turns.Count(turn => turn.Text == BehaviorChatClient.Review) < 2)
            {
                await Task.Delay(25, token);
            }
            using (VerifiedActor.Enter(actor))
            {
                Assert.Equal(revision, (await behavior.ReadState()).Active!.Revision);
            }
            var other = new ActorContext(PrincipalId.New(), "other");
            await Send("list personal reviews", other);
            Assert.Equal("[]", model.Results.Last());
            await Send("remove personal review", other);
            using (VerifiedActor.Enter(actor))
            {
                Assert.True((await behavior.ReadState()).Enabled);
            }
            await Send("remove personal review");
            using (VerifiedActor.Enter(actor))
            {
                var disabled = await behavior.ReadState();
                Assert.False(disabled.Enabled);
                Assert.Equal(source, disabled.Draft!.Source);
            }

            async Task Send(string text, ActorContext? sender = null)
            {
                var principal = sender ?? actor;
                var target = sim.Brain.Get<IChat>(PrincipalPartition.InstanceName(principal.PrincipalId, "main"));
                var accepted = await target.RequestAsync(new SendMessage(CommandId.New(), text, principal), token);
                var terminal = await JournalWait.ForAsync(target, JournalKind.Outgoing,
                    delivery => delivery.Signal is TurnLifecycle life && life.TurnId == accepted.TurnId
                        && life.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled,
                    TimeSpan.FromSeconds(20), cancellationToken: token);
                var lifecycle = Assert.IsType<TurnLifecycle>(terminal.Signal);
                Assert.True(lifecycle.Status == ChatTurnStatus.Completed, lifecycle.Detail);
            }
        }
        finally
        {
            model.ContinueReview.TrySetResult();
            await worker.StopAsync(token);
        }
    }

    private sealed class BehaviorChatClient(string source) : IChatClient
    {
        internal const string Review = "Review complete: no concrete bugs found in the test diff.";
        public TaskCompletionSource ReviewerStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueReview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Results { get; } = new();
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = messages.Last(message => message.Role == ChatRole.User).Text;
            var toolName = request switch
            {
                "save personal review" => "save_behavior",
                "inspect personal review" => "read_behavior",
                "list personal reviews" => "list_behaviors",
                "activate personal review" => "activate_behavior",
                "invoke personal review" => "invoke_behavior",
                "publish personal review" => "publish_behavior_in_this_chat",
                "remove personal review" => "disable_behavior",
                _ => null,
            };
            if (toolName is null)
            {
                if (request.StartsWith("read_repository_diff:", StringComparison.Ordinal))
                {
                    ReviewerStarted.TrySetResult();
                    await ContinueReview.Task.WaitAsync(cancellationToken);
                }
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    request.StartsWith("read_repository_diff:", StringComparison.Ordinal) ? Review : "Hello while the review runs.")
                {
                    FinishReason = ChatFinishReason.Stop,
                };
                yield break;
            }
            var tool = Assert.Single(options!.Tools!.OfType<AIFunction>(), candidate => candidate.Name == toolName);
            var arguments = new AIFunctionArguments();
            if (toolName != "list_behaviors")
            {
                arguments["name"] = "personal-review";
            }
            if (toolName == "save_behavior")
            {
                arguments["source"] = source;
                arguments["inputSignalTypes"] = new[] { nameof(Note) };
                arguments["outputSignalTypes"] = new[] { nameof(Note) };
                arguments["inputPolicy"] = BehaviorInputPolicy.EveryEvent;
            }
            if (toolName == "invoke_behavior")
            {
                arguments["inputType"] = nameof(Note);
                arguments["inputJson"] = "{\"text\":\"Review this actual diff: before => after\"}";
            }
            var result = await tool.InvokeAsync(arguments, cancellationToken);
            Results.Enqueue(result?.ToString() ?? "");
            yield return new ChatResponseUpdate(ChatRole.Assistant, result?.ToString()) { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
