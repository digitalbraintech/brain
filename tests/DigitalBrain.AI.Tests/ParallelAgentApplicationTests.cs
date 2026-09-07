using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.AI.Tests;

public sealed class ParallelAgentApplicationTests
{
    [Fact(Timeout = 30000)]
    public async Task Named_parallel_agent_branches_overlap_and_reuse_both_results_after_restart()
    {
        var clock = new AdvanceableClock();
        var model = new BarrierChatClient();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<IChatClient>(model);
                silo.Services.AddSingleton<TimeProvider>(clock);
            },
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "parallel-agents", actor);
        var completedCalls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = brain.Application("parallel");
        var command = Declare(app, brain, completedCalls, finishHandler);
        await app.InstallAsync("parallel-v1", TestContext.Current.CancellationToken);
        var invocation = await command.SubmitAsync(new AgentRequest("compare"), TestContext.Current.CancellationToken);

        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var serving = simulation.ServeApplicationAsync(app, "parallel-v1", stopping.Token);
            var result = invocation.ResultAsync(TestContext.Current.CancellationToken);
            var started = model.BothStarted.WaitAsync(TestContext.Current.CancellationToken);
            var first = await Task.WhenAny(started, result, serving);
            if (first == result) { await result; }
            if (first == serving) { await serving; }
            await started;
            model.ReleaseBoth();
            var completed = completedCalls.Task.WaitAsync(TestContext.Current.CancellationToken);
            var second = await Task.WhenAny(completed, result, serving);
            if (second == result) { await result; }
            if (second == serving) { await serving; }
            await completed;
            await stopping.CancelAsync();
            await serving;
        }

        Assert.Equal(2, model.CompletedCalls);
        clock.Advance(TimeSpan.FromSeconds(31));
        finishHandler.TrySetResult();
        var resumed = brain.Application("parallel");
        Declare(resumed, brain);
        using var resumedStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var resumedServing = simulation.ServeApplicationAsync(resumed, "parallel-v1", resumedStopping.Token);
        try
        {
            var reply = await invocation.ResultAsync(TestContext.Current.CancellationToken);
            Assert.Equal("left reply | right reply", reply.Text);
            Assert.Equal(2, model.CompletedCalls);
        }
        finally
        {
            await resumedStopping.CancelAsync();
            await resumedServing;
        }
    }

    private static CommandPort<AgentRequest, AgentReply> Declare(
        ApplicationDefinition app,
        DigitalBrainClient brain,
        TaskCompletionSource? completed = null,
        TaskCompletionSource? finish = null)
        => app.Agent("parallel", async (_, run, ct) =>
        {
            var left = run.Branch("left");
            var right = run.Branch("right");
            var replies = await Task.WhenAll(
                left.CallAsync(brain.Get<IAgent>("left"), new AgentRequest("left"), ct),
                right.CallAsync(brain.Get<IAgent>("right"), new AgentRequest("right"), ct));
            completed?.TrySetResult();
            if (finish is not null) { await finish.Task.WaitAsync(ct); }
            return new AgentReply($"{replies[0].Text} | {replies[1].Text}");
        });

    private sealed class BarrierChatClient : IChatClient
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int started;
        private int completed;
        public Task BothStarted => bothStarted.Task;
        public int CompletedCalls => Volatile.Read(ref completed);
        private readonly TaskCompletionSource bothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseBoth() => release.TrySetResult();

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var request = messages.Last(message => message.Role == ChatRole.User).Text;
            if (Interlocked.Increment(ref started) == 2) { bothStarted.TrySetResult(); }
            await release.Task.WaitAsync(cancellationToken);
            Assert.InRange(Interlocked.Increment(ref completed), 1, 2);
            return new(new ChatMessage(ChatRole.Assistant, $"{request} reply"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new(ChatRole.Assistant, response.Messages.Single().Text) { FinishReason = ChatFinishReason.Stop };
        }
        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }

    private sealed class AdvanceableClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }
}

