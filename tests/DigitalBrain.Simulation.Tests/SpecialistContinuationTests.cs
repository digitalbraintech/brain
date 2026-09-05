using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
using DigitalBrain.Sdk;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SpecialistContinuationTests
{
    [Fact]
    public async Task LoginResumesTheExactSpecialistOnceWithNativeReadScopeAndARealLearnedEdge()
    {
        await using var scenario = await ContinuationScenario.StartAsync();
        using var verified = VerifiedActor.Enter(scenario.Actor);
        var action = await scenario.StartLoginAsync();
        var descriptor = Assert.IsType<SpecialistContinuation>(action.SpecialistContinuation);
        Assert.Equal(scenario.Target, descriptor.Target);
        Assert.Equal(ContinuationScenario.Request, descriptor.RequestText);
        Assert.Equal(["provider_read"], descriptor.AllowedToolNames);
        Assert.Null(descriptor.ConnectionRevision);
        var assistant = scenario.Simulation.Brain.Get<IAssistant>("assistant").Id;
        Assert.Single((await scenario.Query(assistant).ReadJournal(JournalKind.Outgoing, 0)).Delta,
            delivery => delivery.Signal is AgentRequest { Text: ContinuationScenario.Request });
        Assert.Empty(await scenario.Query(assistant).ReadSynapses());

        await scenario.AcceptAsync(action);
        await scenario.DeliverLoginAsync();
        var completed = await scenario.AwaitStatusAsync(ChatTurnStatus.Completed);
        Assert.Equal(scenario.Command, completed.CommandId);

        // Re-delivery and a replayed OAuth commit cannot create another model turn.
        await scenario.DeliverLoginAsync();
        await Assert.ThrowsAsync<McpOperationException>(() => scenario.Provider.Logins.AcceptForActorAsync(
            ContinuationScenario.RequestId(action), (_, _, _) => Task.CompletedTask));
        await scenario.Chat.SendAsync(new CompleteUserAction(scenario.Provider.LoginContext!, action.Id, true),
            TestContext.Current.CancellationToken);
        var snapshot = Assert.Single((await scenario.Chat.RequestAsync(new ReadTurns(),
            TestContext.Current.CancellationToken)).Turns);
        Assert.Equal(ChatTurnStatus.Completed, snapshot.Status);
        Assert.Equal("provider evidence", snapshot.Answer);
        Assert.Equal(1, scenario.Model.AssistantCalls);
        var call = Assert.Single(scenario.Model.SpecialistCalls);
        Assert.Equal(scenario.Actor.PrincipalId, call.Actor.PrincipalId);
        Assert.Equal(scenario.Actor, call.Context.Actor);
        Assert.Equal(scenario.Chat.Id, call.Context.Chat);
        Assert.Equal(scenario.Command, call.Context.CommandId);
        Assert.Equal(scenario.Target, call.Context.SpecialistContinuation!.Target);
        Assert.Equal("binding-1", call.Context.SpecialistContinuation.ConnectionRevision);
        Assert.Equal(ContinuationScenario.Request, call.Request);
        Assert.Equal(["provider_read"], call.ToolNames);
        Assert.Equal(1, scenario.Provider.Reads);
        Assert.Equal(0, scenario.Provider.Writes);

        var worker = ChatTurnWorker.ForChat(scenario.Chat.Id);
        var route = Assert.Single(await scenario.Query(worker).ReadSynapses(), edge => edge.Target == scenario.Target);
        Assert.Equal(worker, route.Source);
        Assert.Equal(SynapseKind.Learned, route.Kind);
        Assert.Equal(nameof(AgentRequest), route.SignalType);
        Assert.Equal(1, route.FireCount);
        var delivered = Assert.Single((await scenario.Query(scenario.Target)
            .ReadJournal(JournalKind.Incoming, 0)).Delta, item => item.Signal is AgentRequest);
        Assert.Equal(worker, delivered.Caller);
        Assert.Equal(scenario.Actor.PrincipalId, delivered.Principal);
        Assert.Equal(ContinuationScenario.Request, Assert.IsType<AgentRequest>(delivered.Signal).Text);

        var responses = (await scenario.Chat.ReadJournalAsync(JournalKind.Outgoing,
            cancellationToken: TestContext.Current.CancellationToken)).Delta
            .Select(item => item.Signal).OfType<Responded>().Where(reply => reply.CommandId == scenario.Command).ToArray();
        Assert.Single(responses, reply => reply.UserAction?.Id == action.Id);
        Assert.Single(responses, reply => reply.UserAction is null && reply.Text == "provider evidence");
    }

    [Fact]
    public async Task AnAcceptedCallbackWithoutTheTrustedOAuthCommitCannotResumeTheSpecialist()
    {
        await using var scenario = await ContinuationScenario.StartAsync();
        using var verified = VerifiedActor.Enter(scenario.Actor);
        var action = await scenario.StartLoginAsync();

        await scenario.Chat.SendAsync(new CompleteUserAction(scenario.Provider.LoginContext!, action.Id, true),
            TestContext.Current.CancellationToken);

        var failed = await scenario.AwaitStatusAsync(ChatTurnStatus.Failed);
        Assert.Contains("connection", failed.Detail ?? "", StringComparison.Ordinal);
        Assert.Null(scenario.Provider.Revision);
        Assert.Empty(scenario.Model.SpecialistCalls);
        Assert.Equal(0, scenario.Provider.Reads);
        Assert.Equal(0, scenario.Provider.Writes);
        Assert.Empty(await scenario.Query(ChatTurnWorker.ForChat(scenario.Chat.Id)).ReadSynapses());
    }

    [Fact]
    public async Task CancellingTheWaitingChatPreventsCredentialCommitAndSpecialistResume()
    {
        await using var scenario = await ContinuationScenario.StartAsync();
        using var verified = VerifiedActor.Enter(scenario.Actor);
        var action = await scenario.StartLoginAsync();
        var request = ContinuationScenario.RequestId(action);
        Assert.True(scenario.Provider.Logins.TryBegin(request, out _));
        Assert.True(scenario.Provider.Logins.TryClaim(request));

        await scenario.Chat.SendAsync(new CancelTurn(CommandId.New(), scenario.Turn, scenario.Actor),
            TestContext.Current.CancellationToken);
        await scenario.AwaitStatusAsync(ChatTurnStatus.Cancelled);
        var committed = false;
        await Assert.ThrowsAsync<McpOperationException>(() => scenario.Provider.Logins.AcceptForActorAsync(
            request, (_, _, commit) => { commit(() => committed = true); return Task.CompletedTask; }));
        await scenario.Chat.SendAsync(new CompleteUserAction(scenario.Provider.LoginContext!, action.Id, true),
            TestContext.Current.CancellationToken);
        var snapshot = Assert.Single((await scenario.Chat.RequestAsync(new ReadTurns(),
            TestContext.Current.CancellationToken)).Turns);

        Assert.False(committed);
        Assert.Equal(ChatTurnStatus.Cancelled, snapshot.Status);
        Assert.Empty(scenario.Model.SpecialistCalls);
        Assert.Equal(1, scenario.Model.AssistantCalls);
        Assert.Equal(0, scenario.Provider.Reads);
        Assert.Equal(0, scenario.Provider.Writes);
        Assert.Empty(await scenario.Query(ChatTurnWorker.ForChat(scenario.Chat.Id)).ReadSynapses());
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("scope")]
    public async Task BindingChangesAfterLoginResolutionFailBeforeTheResumedModelOrToolRuns(string change)
    {
        await using var scenario = await ContinuationScenario.StartAsync(pauseResume: true);
        using var verified = VerifiedActor.Enter(scenario.Actor);
        var action = await scenario.StartLoginAsync();
        await scenario.AcceptAsync(action);
        try
        {
            await scenario.DeliverLoginAsync();
            await scenario.Provider.ResumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(15),
                TestContext.Current.CancellationToken);
            if (change == "revision")
            {
                scenario.Provider.Revision = "binding-2";
            }
            else
            {
                scenario.Provider.ReadGranted = false;
            }
            scenario.Provider.ContinueResume.TrySetResult();

            var failure = await scenario.AwaitStatusAsync(ChatTurnStatus.Failed);
            Assert.Contains(change, failure.Detail ?? "", StringComparison.Ordinal);
            Assert.Empty(scenario.Model.SpecialistCalls);
            Assert.Equal(1, scenario.Model.AssistantCalls);
            Assert.Equal(0, scenario.Provider.Reads);
            Assert.Equal(0, scenario.Provider.Writes);
            Assert.Empty(await scenario.Query(ChatTurnWorker.ForChat(scenario.Chat.Id)).ReadSynapses());
        }
        finally
        {
            scenario.Provider.ContinueResume.TrySetResult();
        }
    }

    [Theory]
    [InlineData("gmail", "gmail_search_threads", false)]
    [InlineData("salesforce", "salesforce_soql_query", false)]
    [InlineData("unrelated-provider", "legacy_read", true)]
    public async Task OldProviderLoginRecordsCannotResumeRetiredWrappersWhileOtherContinuationsStillWork(
        string provider, string legacyToolName, bool shouldResume)
    {
        await using var scenario = await ContinuationScenario.StartAsync(
            legacyProvider: provider, legacyToolName: legacyToolName);
        using var verified = VerifiedActor.Enter(scenario.Actor);
        var action = await scenario.StartLoginAsync();
        Assert.Equal(provider, action.Provider);
        Assert.Null(action.SpecialistContinuation);
        Assert.Equal([legacyToolName], action.ResumeToolNames);

        await scenario.AcceptAsync(action);
        await scenario.DeliverLoginAsync();
        await scenario.AwaitStatusAsync(ChatTurnStatus.Completed);
        var snapshot = Assert.Single((await scenario.Chat.RequestAsync(new ReadTurns(),
            TestContext.Current.CancellationToken)).Turns);

        if (shouldResume)
        {
            Assert.Equal("legacy read evidence", snapshot.Answer);
            Assert.Equal(2, scenario.Model.AssistantCalls);
            Assert.Equal(1, scenario.Provider.Reads);
        }
        else
        {
            Assert.Contains("repeat your request", snapshot.Answer ?? "", StringComparison.Ordinal);
            Assert.Equal(1, scenario.Model.AssistantCalls);
            Assert.Equal(0, scenario.Provider.Reads);
        }
        Assert.Empty(scenario.Model.SpecialistCalls);
        Assert.Equal(0, scenario.Provider.Writes);
        Assert.Empty(await scenario.Query(ChatTurnWorker.ForChat(scenario.Chat.Id)).ReadSynapses());
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("foreign-owner")]
    [InlineData("missing-revision")]
    public async Task CorruptStoredContinuationIsRejectedByTheWorkerBeforeDelegation(string corruption)
    {
        await using var scenario = await ContinuationScenario.StartAsync();
        var descriptor = new SpecialistContinuation(scenario.Target, ContinuationScenario.Request,
            ["provider_read"], "binding-1");
        descriptor = corruption switch
        {
            "scope" => descriptor with { AllowedToolNames = ["provider_write"] },
            "foreign-owner" => descriptor with
            {
                Target = new NeuronId(scenario.Target.Type, new OwnerId("other"), scenario.Target.Name),
            },
            _ => descriptor with { ConnectionRevision = null },
        };
        var goal = new ChatTurnGoal(Guid.NewGuid(), scenario.Command, ContinuationScenario.Request,
            scenario.Actor, scenario.Chat.Id, ["provider_read"], "completed-action", descriptor,
            ConversationId: CorrelationId.New());
        using var verified = VerifiedActor.Enter(scenario.Actor);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Simulation.Grains
            .GetGrain<IChatTurnWorker>(ChatTurnWorker.ForChat(scenario.Chat.Id).ToGrainId())
            .RunAsync(goal, TestContext.Current.CancellationToken));

        Assert.Contains("continuation", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Model.AssistantCalls);
        Assert.Empty(scenario.Model.SpecialistCalls);
        Assert.Equal(0, scenario.Provider.Reads);
        Assert.Empty(await scenario.Query(ChatTurnWorker.ForChat(scenario.Chat.Id)).ReadSynapses());
    }
}

[Alias("DigitalBrain.Simulation.Tests.IContinuationProbe")]
public interface IContinuationProbe : IAgent;

[GrainType("continuationprobe")]
internal sealed class ContinuationProbe(NeuronRuntime runtime, IChatClient client,
    ContinuationProvider provider, ContinuationProbeLogins logins) : Agent(runtime, client), IContinuationProbe
{
    internal const string Purpose = "You are the specialist continuation integration probe.";
    protected override string Instructions => Purpose;
    protected override ValueTask<IReadOnlyList<AITool>> PrepareToolsAsync(
        AgentToolContext context, CancellationToken cancellationToken)
        => provider.PrepareAsync(context, logins, cancellationToken);
}

// This fake supplies the provider-owned binding/scope check. Login control, durable
// chat state, restricted tools, signal delivery and model scheduling are production code.
internal sealed class ContinuationProvider(bool pauseResume, string? legacyProvider, string? legacyToolName)
{
    public ContinuationProbeLogins Logins { get; set; } = null!;
    public AgentTurnContext? LoginContext { get; private set; }
    public ActorContext? ConnectedActor { get; set; }
    public string? Revision { get; set; }
    public bool ReadGranted { get; set; } = true;
    public string? LegacyProvider { get; } = legacyProvider;
    public string? LegacyToolName { get; } = legacyToolName;
    public int Reads;
    public int Writes;
    public TaskCompletionSource ResumeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ContinueResume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<IReadOnlyList<AITool>> PrepareAsync(
        AgentToolContext context, ContinuationProbeLogins logins, CancellationToken cancellationToken)
    {
        var turn = Assert.IsType<AgentTurnContext>(AgentTurnContext.Current);
        Assert.Equal(turn.Actor.PrincipalId, VerifiedActor.Current?.PrincipalId);
        Assert.Equal(turn.Actor.PrincipalId, context.Principal);
        Assert.Equal(context.Agent, turn.SpecialistRequest!.Target);
        if (Revision is null)
        {
            // Simulate the persisted shape minted before provider specialist migration.
            LoginContext = LegacyProvider is null ? turn : turn with { SpecialistRequest = null };
            using var login = AgentTurnContext.Enter(LoginContext);
            logins.Require([LegacyToolName ?? "provider_read"], "read", cancellationToken);
            throw new McpOperationException("Connect the test provider to complete this read.");
        }
        if (pauseResume)
        {
            ResumeStarted.TrySetResult();
            await ContinueResume.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        if (ConnectedActor?.PrincipalId != turn.Actor.PrincipalId || turn.SpecialistContinuation?.ConnectionRevision != Revision)
        {
            throw new McpOperationException("The provider binding revision changed.");
        }
        if (!ReadGranted)
        {
            throw new McpOperationException("The provider read scope changed.");
        }

        return [
            AIFunctionFactory.Create(() => { Interlocked.Increment(ref Reads); return "provider evidence"; },
                new AIFunctionFactoryOptions { Name = "provider_read" }),
            AIFunctionFactory.Create(() => { Interlocked.Increment(ref Writes); return "forbidden write"; },
                new AIFunctionFactoryOptions { Name = "provider_write" }),
        ];
    }
}

internal sealed class ContinuationProbeLogins(IServiceProvider services, ContinuationProvider provider)
    : BrowserLogins(new(provider.LegacyProvider ?? "continuation-probe", "Test provider", "test-oauth", "/test/login", "/test/callback",
        "Connect the test provider to complete this read."), services)
{
    protected override Uri PublicOrigin => new("http://localhost:5080");
    protected override string? GetConnectionRevision(AgentTurnContext context)
    {
        Assert.Equal(context.Actor.PrincipalId, VerifiedActor.Current?.PrincipalId);
        return provider.ConnectedActor?.PrincipalId == context.Actor.PrincipalId ? provider.Revision : null;
    }
}

internal sealed class ContinuationLegacyReadTools(ContinuationProvider provider) : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        context.RequireActive();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<AITool>>([
            AIFunctionFactory.Create(() =>
            {
                Assert.Equal([provider.LegacyToolName!],
                    Assert.IsType<string[]>(AgentTurnContext.Current?.AllowedToolNames));
                Interlocked.Increment(ref provider.Reads);
                return "legacy read evidence";
            }, new AIFunctionFactoryOptions { Name = provider.LegacyToolName! }),
        ]);
    }
}

internal sealed record ContinuationModelCall(ActorContext Actor, AgentTurnContext Context,
    string Request, string[] ToolNames);

internal sealed class ContinuationChatClient : IChatClient
{
    public int AssistantCalls;
    public ConcurrentQueue<ContinuationModelCall> SpecialistCalls { get; } = new();
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages.ToArray();
        var tools = options?.Tools ?? [];
        string response;
        if (history.Any(message => message.Role == ChatRole.System && message.Text == ContinuationProbe.Purpose))
        {
            var tool = Assert.Single(tools.OfType<AIFunction>());
            Assert.Equal("provider_read", tool.Name);
            SpecialistCalls.Enqueue(new(Assert.IsType<ActorContext>(VerifiedActor.Current),
                Assert.IsType<AgentTurnContext>(AgentTurnContext.Current),
                history.Last(message => message.Role == ChatRole.User).Text,
                tools.Select(item => item.Name).ToArray()));
            response = (await tool.InvokeAsync([], cancellationToken).ConfigureAwait(true))?.ToString() ?? "";
        }
        else
        {
            Interlocked.Increment(ref AssistantCalls);
            if (AgentTurnContext.Current?.AllowedToolNames is not null)
            {
                response = (await Assert.Single(tools.OfType<AIFunction>())
                    .InvokeAsync([], cancellationToken).ConfigureAwait(true))?.ToString() ?? "";
            }
            else
            {
                var tool = Assert.Single(tools.OfType<AIFunction>(), candidate => candidate.Name == "ask_login_probe");
                response = (await tool.InvokeAsync(new AIFunctionArguments { ["request"] = ContinuationScenario.Request },
                    cancellationToken).ConfigureAwait(true))?.ToString() ?? "";
            }
        }
        yield return new ChatResponseUpdate(ChatRole.Assistant, response) { FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}

internal sealed class ContinuationScenario(BrainSimulation simulation, ContinuationProvider provider,
    ContinuationChatClient model) : IAsyncDisposable
{
    public const string Request = "Read my release messages";
    public BrainSimulation Simulation { get; } = simulation;
    public ContinuationProvider Provider { get; } = provider;
    public ContinuationChatClient Model { get; } = model;
    public ActorContext Actor { get; } = new(new PrincipalId(Guid.NewGuid()), "continuation-owner");
    public CommandId Command { get; } = CommandId.New();
    public TurnId Turn { get; private set; }
    public NeuronReference<IChat> Chat => Simulation.Brain.Get<IChat>(PrincipalPartition.InstanceName(Actor.PrincipalId, "main"));
    public NeuronId Target => NeuronId.For<IContinuationProbe>(Chat.Id.Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "provider"));
    public INeuronQuery Query(NeuronId id) => Simulation.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    public static async Task<ContinuationScenario> StartAsync(
        bool pauseResume = false, string? legacyProvider = null, string? legacyToolName = null)
    {
        var provider = new ContinuationProvider(pauseResume, legacyProvider, legacyToolName);
        var model = new ContinuationChatClient();
        var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(DigitalBrain.Execution.ExecutionModule), typeof(UIModule), typeof(AIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton(provider);
                silo.Services.AddSingleton<IChatClient>(model);
                silo.Services.AddSingleton(sp => provider.Logins = new ContinuationProbeLogins(sp, provider));
                silo.Services.AddSingleton<IUserActionSource>(sp => sp.GetRequiredService<ContinuationProbeLogins>());
                silo.Services.AddSingleton<IAgentToolSource>(new AgentDelegation<IContinuationProbe>(
                    "ask_login_probe", "Ask the authenticated provider specialist.", "provider"));
                if (legacyProvider is not null)
                {
                    silo.Services.AddSingleton<IAgentToolSource>(new ContinuationLegacyReadTools(provider));
                }
            },
        });
        return new(simulation, provider, model);
    }

    public async Task<UserActionRequest> StartLoginAsync()
    {
        Turn = (await Chat.RequestAsync(new SendMessage(Command, Request, Actor),
            TestContext.Current.CancellationToken)).TurnId;
        await AwaitStatusAsync(ChatTurnStatus.WaitingForUser);
        var snapshot = Assert.Single((await Chat.RequestAsync(new ReadTurns(), TestContext.Current.CancellationToken)).Turns);
        return Assert.IsType<UserActionRequest>(snapshot.UserAction);
    }

    public async Task AcceptAsync(UserActionRequest action)
    {
        var request = RequestId(action);
        Assert.True(Provider.Logins.TryBegin(request, out var scope));
        Assert.Equal("read", scope);
        Assert.True(Provider.Logins.TryClaim(request));
        await Provider.Logins.AcceptForActorAsync(request, (context, _, commit) =>
        {
            Assert.Equal(Actor, context.Actor);
            Assert.Equal(Chat.Id, context.Chat);
            Assert.Equal(Command, context.CommandId);
            commit(() => { Provider.ConnectedActor = context.Actor; Provider.Revision = "binding-1"; });
            return Task.CompletedTask;
        });
    }

    public async Task DeliverLoginAsync()
    {
        // Browser login delivery is background work, without a request's ambient actor.
        using var background = VerifiedActor.Enter(null);
        await Provider.Logins.DeliverAsync(TestContext.Current.CancellationToken);
    }

    public async Task<TurnLifecycle> AwaitStatusAsync(ChatTurnStatus status)
    {
        var delivery = await JournalWait.ForAsync(Chat, JournalKind.Outgoing,
            item => item.Signal is TurnLifecycle life && life.TurnId == Turn &&
                (life.Status == status || life.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled),
            TimeSpan.FromSeconds(25), cancellationToken: TestContext.Current.CancellationToken);
        var lifecycle = Assert.IsType<TurnLifecycle>(delivery.Signal);
        Assert.True(lifecycle.Status == status, lifecycle.Detail ?? lifecycle.Status.ToString());
        return lifecycle;
    }

    public static string RequestId(UserActionRequest action) => new Uri(action.LoginUrl).Query.Split('=')[1];
    public ValueTask DisposeAsync() => Simulation.DisposeAsync();
}
