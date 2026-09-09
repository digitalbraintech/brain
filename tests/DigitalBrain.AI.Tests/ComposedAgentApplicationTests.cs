using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using DigitalBrain.Chat;
using DigitalBrain.Abstractions.Journals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.AI.Tests;

public sealed class ComposedAgentApplicationTests
{
    [Fact(Timeout = 30000)]
    public async Task Chat_brainstorm_rule_invokes_the_reusable_agent_and_replies_to_the_originating_conversation()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new StrictCompositionChatClient();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule), typeof(UIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "brainstorm-chat", actor);
        var app = brain.Application("brainstorming");
        DeclareComposition(app, brain);
        app.OnUserMessageContaining("chat", "brainstorm:", async (message, run, token) =>
        {
            var idea = message.Text[(message.Text.IndexOf("brainstorm:", StringComparison.OrdinalIgnoreCase) + 11)..].Trim();
            var reply = await run.CallAsync(brain.Get<IAgent>("brainstorm"), new AgentRequest(idea), token);
            return reply.Text;
        });
        await app.InstallAsync("composition-chat-r1", ct);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var worker = simulation.ServeApplicationAsync(app, "composition-chat-r1", stopping.Token);
        try
        {
            using var verified = VerifiedActor.Enter(actor);
            var command = await new WorkspaceInject(brain, simulation.Grains).InjectUserMessage(
                "main", "Please BRAINSTORM: A quiet neighborhood tool library.", actor, ct);
            var composer = brain.Get<IComposer>(IComposer.DefaultInstanceName);
            Responded? result = null;
            while (result is null)
            {
                var journal = await composer.ReadJournalAsync(JournalKind.Outgoing, 0, ct);
                result = journal.Delta.Select(delivery => delivery.Signal).OfType<Responded>()
                    .SingleOrDefault(response => response.CommandId == command);
                if (result is null) { await Task.Delay(25, ct); }
            }
            Assert.Equal("Final: start with one lending shelf; ask who maintains it.", result.Text);
            Assert.Equal(3, model.CompletedCalls);
        }
        finally { await stopping.CancelAsync(); await worker; }
    }

    [Fact(Timeout = 30000)]
    public async Task Installed_agent_is_addressable_through_the_agent_neuron_contract()
    {
        var model = new StrictCompositionChatClient();
        await using var simulation = await StartAsync(model);
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "addressable-composition", actor);
        var app = brain.Application("brainstorming");
        DeclareComposition(app, brain);
        await app.InstallAsync("composition-v1", TestContext.Current.CancellationToken);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "composition-v1", stopping.Token);
        try
        {
            var reply = await brain.Get<IAgent>("brainstorm").RequestAsync(
                new AgentRequest("A quiet neighborhood tool library."),
                TestContext.Current.CancellationToken);

            Assert.Equal("Final: start with one lending shelf; ask who maintains it.", reply.Text);
            Assert.Equal(3, model.CompletedCalls);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Replay_rejects_changed_agent_request_at_the_same_effect_position()
    {
        var clock = new AdvanceableClock();
        var model = new SingleRequestChatClient("first request", "first reply");
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<IChatClient>(model);
                silo.Services.AddSingleton<TimeProvider>(clock);
            },
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "composition-conflict", actor);
        var firstFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = brain.Application("conflict");
        var target = brain.Get<IAgent>("proposer");
        var command = app.Agent("compose", async (_, run, ct) =>
        {
            var reply = await run.CallAsync(target, new AgentRequest("first request"), ct);
            firstFinished.TrySetResult();
            await neverContinue.Task.WaitAsync(ct);
            return reply;
        });
        await app.InstallAsync("conflict-v1", TestContext.Current.CancellationToken);
        var invocation = await command.SubmitAsync(new AgentRequest("start"), TestContext.Current.CancellationToken);
        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var serving = simulation.ServeApplicationAsync(app, "conflict-v1", stopping.Token);
            await firstFinished.Task.WaitAsync(TestContext.Current.CancellationToken);
            await stopping.CancelAsync();
            await serving;
        }

        clock.Advance(TimeSpan.FromSeconds(31));
        var changed = brain.Application("conflict");
        changed.Agent("compose", (request, run, ct) =>
            run.CallAsync(target, new AgentRequest("changed request"), ct));
        using var resumedStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var resumed = simulation.ServeApplicationAsync(changed, "conflict-v1", resumedStopping.Token);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                invocation.ResultAsync(TestContext.Current.CancellationToken));
            Assert.Contains("effect", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, model.CompletedCalls);
        }
        finally
        {
            await resumedStopping.CancelAsync();
            await resumed;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Restarted_worker_reuses_checkpointed_proposal_before_continuing_composition()
    {
        var clock = new AdvanceableClock();
        var model = new StrictCompositionChatClient();
        await using var simulation = await StartAsync(model, clock);
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "composition-replay", actor);
        var proposerFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueComposition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstApp = brain.Application("brainstorming");
        var first = DeclareComposition(firstApp, brain, async (run, ct) =>
        {
            proposerFinished.TrySetResult();
            await continueComposition.Task.WaitAsync(ct);
        });
        await firstApp.InstallAsync("composition-v1", TestContext.Current.CancellationToken);
        var invocation = await first.SubmitAsync(
            new AgentRequest("A quiet neighborhood tool library."),
            TestContext.Current.CancellationToken);
        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var serving = simulation.ServeApplicationAsync(firstApp, "composition-v1", stopping.Token);
            await proposerFinished.Task.WaitAsync(TestContext.Current.CancellationToken);
            await stopping.CancelAsync();
            await serving;
        }

        Assert.Equal(1, model.CompletedCalls);
        clock.Advance(TimeSpan.FromSeconds(31));
        continueComposition.TrySetResult();
        var resumedApp = brain.Application("brainstorming");
        DeclareComposition(resumedApp, brain);
        using var resumedStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var resumedServing = simulation.ServeApplicationAsync(resumedApp, "composition-v1", resumedStopping.Token);
        try
        {
            var reply = await invocation.ResultAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Final: start with one lending shelf; ask who maintains it.", reply.Text);
            Assert.Equal(3, model.CompletedCalls);
        }
        finally
        {
            await resumedStopping.CancelAsync();
            await resumedServing;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Application_composes_proposer_critic_and_synthesizer_through_agent_contract()
    {
        var model = new StrictCompositionChatClient();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "composition", actor);
        var app = brain.Application("brainstorming");
        var proposer = brain.Get<IAgent>("proposer");
        var critic = brain.Get<IAgent>("critic");
        var synthesizer = brain.Get<IAgent>("synthesizer");
        var composed = app.Agent("brainstorm", async (request, run, ct) =>
        {
            var proposal = await run.CallAsync(proposer, request, ct);
            var criticism = await run.CallAsync(critic, new AgentRequest($"""
                Critique the proposal against the original idea.
                Idea: {request.Text}
                Proposal: {proposal.Text}
                """), ct);
            return await run.CallAsync(synthesizer, new AgentRequest($"""
                Return a revised proposal and open questions.
                Idea: {request.Text}
                Proposal: {proposal.Text}
                Criticism: {criticism.Text}
                """), ct);
        });
        await app.InstallAsync("composition-v1", TestContext.Current.CancellationToken);
        var invocation = await composed.SubmitAsync(
            new AgentRequest("A quiet neighborhood tool library."),
            TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "composition-v1", stopping.Token);
        try
        {
            var reply = await invocation.ResultAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Final: start with one lending shelf; ask who maintains it.", reply.Text);
            Assert.Equal(3, model.CompletedCalls);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    private static Task<BrainSimulation> StartAsync(StrictCompositionChatClient model, TimeProvider? clock = null)
        => BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<IChatClient>(model);
                if (clock is not null)
                {
                    silo.Services.AddSingleton(clock);
                }
            },
        });

    private static CommandPort<AgentRequest, AgentReply> DeclareComposition(
        ApplicationDefinition app,
        DigitalBrainClient brain,
        Func<ApplicationExecutionContext, CancellationToken, Task>? afterProposal = null)
    {
        var proposer = brain.Get<IAgent>("proposer");
        var critic = brain.Get<IAgent>("critic");
        var synthesizer = brain.Get<IAgent>("synthesizer");
        return app.Agent("brainstorm", async (request, run, ct) =>
        {
            var proposal = await run.CallAsync(proposer, request, ct);
            if (afterProposal is not null)
            {
                await afterProposal(run, ct);
            }
            var criticism = await run.CallAsync(critic, new AgentRequest($"""
                Critique the proposal against the original idea.
                Idea: {request.Text}
                Proposal: {proposal.Text}
                """), ct);
            return await run.CallAsync(synthesizer, new AgentRequest($"""
                Return a revised proposal and open questions.
                Idea: {request.Text}
                Proposal: {proposal.Text}
                Criticism: {criticism.Text}
                """), ct);
        });
    }

    private sealed class StrictCompositionChatClient : IChatClient
    {
        private int _call;
        public int CompletedCalls => Volatile.Read(ref _call);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = messages.Last(message => message.Role == ChatRole.User).Text;
            var call = Interlocked.Increment(ref _call);
            var response = call switch
            {
                1 when request == "A quiet neighborhood tool library."
                    => "Proposal: begin with a shared shelf for drills and ladders.",
                2 when request == """
                    Critique the proposal against the original idea.
                    Idea: A quiet neighborhood tool library.
                    Proposal: Proposal: begin with a shared shelf for drills and ladders.
                    """
                    => "Criticism: maintenance ownership is unspecified.",
                3 when request == """
                    Return a revised proposal and open questions.
                    Idea: A quiet neighborhood tool library.
                    Proposal: Proposal: begin with a shared shelf for drills and ladders.
                    Criticism: Criticism: maintenance ownership is unspecified.
                    """
                    => "Final: start with one lending shelf; ask who maintains it.",
                _ => throw new InvalidOperationException($"Unexpected model call {call}: {request}"),
            };
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate(message.Role, message.Text)
                {
                    FinishReason = ChatFinishReason.Stop,
                };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class AdvanceableClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private sealed class SingleRequestChatClient(string expectedRequest, string response) : IChatClient
    {
        private int _calls;
        public int CompletedCalls => Volatile.Read(ref _calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages.Last(message => message.Role == ChatRole.User).Text;
            Assert.Equal(expectedRequest, request);
            Assert.Equal(1, Interlocked.Increment(ref _calls));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var result = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, result.Messages.Single().Text)
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}

