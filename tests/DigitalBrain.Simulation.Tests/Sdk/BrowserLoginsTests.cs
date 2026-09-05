using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Sdk;

public sealed class BrowserLoginsTests
{
    private static readonly OwnerId Owner = new("dev");
    private static readonly ActorContext Actor = new(new PrincipalId(Guid.NewGuid()), "vlad");

    [Fact]
    public void Require_mints_one_login_per_turn_and_a_write_drops_the_resume_set()
    {
        var (logins, _) = Rail();
        using var turn = EnterTurn();

        var read = logins.Require(["read_tool"], null, CancellationToken.None);
        var write = logins.Require([], "compose", CancellationToken.None);

        Assert.Equal("test", read.Provider);
        Assert.StartsWith("http://localhost:5080/integrations/test/login?request=", read.LoginUrl, StringComparison.Ordinal);
        Assert.Same(read.Id, write.Id);
        Assert.Empty(write.ResumeToolNames);
        Assert.Same(write, logins.Find(Owner, turn.Context.CommandId));
    }

    [Fact]
    public void Require_refuses_calls_outside_an_authenticated_turn_or_before_configuration()
    {
        var (logins, _) = Rail();
        Assert.Throws<McpOperationException>(() => logins.Require([], null, CancellationToken.None));

        var (unconfigured, _) = Rail(configured: false);
        using var turn = EnterTurn();
        Assert.Throws<McpOperationException>(() => unconfigured.Require([], null, CancellationToken.None));
    }

    [Fact]
    public async Task A_login_is_opened_once_claimed_once_committed_once_and_delivered_once()
    {
        var (logins, continuation) = Rail();
        string request;
        AgentTurnContext context;
        using (var turn = EnterTurn())
        {
            context = turn.Context;
            request = RequestOf(logins.Require(["read_tool"], "compose", CancellationToken.None));
        }

        Assert.False(logins.TryClaim(request));
        Assert.True(logins.TryBegin(request, out var scope));
        Assert.Equal("compose", scope);
        Assert.False(logins.TryBegin(request, out _));
        Assert.True(logins.TryClaim(request));
        Assert.False(logins.TryClaim(request));

        var committed = false;
        await logins.AcceptAsync(request, (owner, acceptedScope, commit) =>
        {
            Assert.Equal(Owner, owner);
            Assert.Equal("compose", acceptedScope);
            commit(() => committed = true);
            return Task.CompletedTask;
        });
        Assert.True(committed);
        await Assert.ThrowsAsync<McpOperationException>(() => logins.AcceptAsync(request, static (_, _, _) => Task.CompletedTask));

        continuation.Waiting = false;
        await logins.DeliverAsync(CancellationToken.None);
        Assert.Empty(continuation.Completed);

        continuation.Waiting = true;
        await logins.DeliverAsync(CancellationToken.None);
        await logins.DeliverAsync(CancellationToken.None);
        var (deliveredContext, accepted) = Assert.Single(continuation.Completed);
        Assert.Equal(context, deliveredContext);
        Assert.True(accepted);
    }

    [Fact]
    public async Task A_rejected_or_cancelled_login_never_publishes_and_resumes_the_turn_as_denied()
    {
        var (logins, continuation) = Rail();
        string rejected;
        string cancelled;
        AgentTurnContext cancelledTurn;
        using (var turn = EnterTurn())
        {
            rejected = RequestOf(logins.Require([], null, CancellationToken.None));
        }

        using (var turn = EnterTurn())
        {
            cancelledTurn = turn.Context;
            cancelled = RequestOf(logins.Require([], null, CancellationToken.None));
        }

        Assert.True(logins.TryBegin(rejected, out _));
        Assert.True(logins.TryClaim(rejected));
        logins.Reject(rejected);
        await Assert.ThrowsAsync<McpOperationException>(() => logins.AcceptAsync(rejected, static (_, _, _) => Task.CompletedTask));

        Assert.True(logins.TryBegin(cancelled, out _));
        Assert.True(logins.TryClaim(cancelled));
        logins.Cancel(cancelledTurn);
        var published = false;
        await Assert.ThrowsAsync<McpOperationException>(() => logins.AcceptAsync(cancelled, (_, _, commit) =>
        {
            commit(() => published = true);
            return Task.CompletedTask;
        }));
        Assert.False(published);

        await logins.DeliverAsync(CancellationToken.None);
        var delivered = Assert.Single(continuation.Completed);
        Assert.False(delivered.Accepted);
    }

    [Fact]
    public void Specialist_login_keeps_exact_target_request_and_native_read_scope()
    {
        var (logins, _) = Rail();
        var target = new NeuronId("probe", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "application"));
        using var turn = EnterTurn(new SpecialistRequest(target, "read current evidence"));
        var action = logins.Require(["native_read", "native_read"], null, CancellationToken.None);

        var resume = Assert.IsType<SpecialistContinuation>(action.SpecialistContinuation);
        Assert.Equal(target, resume.Target);
        Assert.Equal("read current evidence", resume.RequestText);
        Assert.Equal(["native_read"], resume.AllowedToolNames);
        Assert.Null(resume.ConnectionRevision);
        Assert.Null(logins.ResolveSpecialistContinuation(turn.Context, action.Id));

        var write = logins.Require([], "compose", CancellationToken.None);
        Assert.Null(write.SpecialistContinuation);
        Assert.Empty(write.ResumeToolNames);
    }

    [Fact]
    public async Task Accepted_login_waits_for_provider_readiness_without_reopening_or_repeating_the_commit()
    {
        var (logins, continuation) = Rail();
        using var turn = EnterTurn();
        var action = logins.Require(["read_tool"], "repository", CancellationToken.None);
        var request = RequestOf(action);
        Assert.True(logins.TryBegin(request, out _));
        Assert.True(logins.TryClaim(request));
        var commits = 0;
        await logins.AcceptForActorAsync(request, (_, _, commit) => { commit(() => commits++); return Task.CompletedTask; });
        logins.WaitingMessage = "Connected. Waiting for signed delivery.";
        await logins.DeliverAsync(CancellationToken.None);
        Assert.Empty(continuation.Completed);
        var waiting = Assert.IsType<UserActionRequest>(logins.Find(Owner, turn.Context.CommandId));
        Assert.Equal("readiness", waiting.Stage);
        Assert.Equal(logins.WaitingMessage, waiting.Message);
        Assert.Equal(action.Id, waiting.Id);
        Assert.False(logins.TryBegin(request, out _));
        logins.WaitingMessage = null;
        await logins.DeliverAsync(CancellationToken.None);
        await logins.DeliverAsync(CancellationToken.None);
        Assert.True(Assert.Single(continuation.Completed).Accepted);
        Assert.Equal(1, commits);
    }

    [Fact]
    public void Recover_remints_only_missing_opted_in_capability_and_preserves_exact_setup_scope()
    {
        var (original, _) = Rail(recoverable: true);
        using var turn = EnterTurn();
        var intent = new SetupContinuation("connect_repository", "https://github.com/intochat/digitalbrain", "announce-ci");
        var action = original.Require(["connect_repository"], intent.Scope, CancellationToken.None, intent);
        Assert.Null(original.Recover(turn.Context, action));
        var (restarted, _) = Rail(recoverable: true);
        var replacement = Assert.IsType<UserActionRequest>(restarted.Recover(turn.Context, action));
        Assert.NotEqual(action.Id, replacement.Id);
        Assert.NotEqual(action.LoginUrl, replacement.LoginUrl);
        Assert.Equal(action.Scope, replacement.Scope);
        Assert.Equal(action.ResumeToolNames, replacement.ResumeToolNames);
        Assert.Equal(intent, replacement.SetupContinuation);
        Assert.False(restarted.TryBegin(RequestOf(action), out _));
        Assert.True(restarted.TryBegin(RequestOf(replacement), out var scope));
        Assert.Equal(action.Scope, scope);
        restarted.Cancel(turn.Context);
        Assert.Null(restarted.Recover(turn.Context, replacement));
    }

    [Fact]
    public void Recover_refuses_other_actors_providers_resumed_turns_and_default_providers()
    {
        var (original, _) = Rail(recoverable: true);
        using var turn = EnterTurn();
        var action = original.Require(["connect_repository"], "repo", CancellationToken.None);
        var (restarted, _) = Rail(recoverable: true);
        var (standard, _) = Rail();
        Assert.Null(standard.Recover(turn.Context, action));
        Assert.Null(restarted.Recover(turn.Context, action with { Provider = "other" }));
        Assert.Null(restarted.Recover(turn.Context with { AllowedToolNames = ["connect_repository"] }, action));
        Assert.Null(restarted.Recover(turn.Context with { Actor = new(new PrincipalId(Guid.NewGuid()), "other") }, action));
        Assert.Null(restarted.Recover(turn.Context, action with { ResumeToolNames = [] }));
    }

    [Fact]
    public async Task Only_accepted_login_resolves_the_same_actor_and_current_binding_once()
    {
        var (logins, _) = Rail();
        var target = new NeuronId("probe", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "application"));
        using var turn = EnterTurn(new SpecialistRequest(target, "read evidence"));
        var action = logins.Require(["native_read"], null, CancellationToken.None);
        var request = RequestOf(action);
        Assert.True(logins.TryBegin(request, out _));
        Assert.True(logins.TryClaim(request));
        await logins.AcceptForActorAsync(request, (context, _, commit) =>
        {
            Assert.Equal(Actor, context.Actor);
            Assert.Equal(target, context.SpecialistRequest!.Target);
            commit(() => logins.Revision = "accepted-binding");
            return Task.CompletedTask;
        });

        var resume = Assert.IsType<SpecialistContinuation>(logins.ResolveSpecialistContinuation(turn.Context, action.Id));
        Assert.Equal("accepted-binding", resume.ConnectionRevision);
        Assert.Null(logins.ResolveSpecialistContinuation(turn.Context with { CommandId = CommandId.New() }, action.Id));
        Assert.Null(logins.ResolveSpecialistContinuation(turn.Context with
        {
            Actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "other"),
        }, action.Id));
        await logins.DeliverAsync(CancellationToken.None);
        Assert.Null(logins.ResolveSpecialistContinuation(turn.Context, action.Id));
    }

    [Fact]
    public void Specialist_login_rejects_foreign_targets_and_repeated_authorization()
    {
        var (logins, _) = Rail();
        var foreign = new NeuronId("probe", Owner, PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), "application"));
        using (EnterTurn(new SpecialistRequest(foreign, "read")))
        {
            Assert.Throws<McpOperationException>(() => logins.Require(["native_read"], null, CancellationToken.None));
        }
        var target = new NeuronId("probe", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "application"));
        using (EnterTurn(new SpecialistRequest(target, "read"), ["native_read"]))
        {
            Assert.Throws<McpOperationException>(() => logins.Require(["native_read"], null, CancellationToken.None));
        }
        using (EnterTurn(new SpecialistRequest(target, new string('x', 16001))))
        {
            Assert.Throws<McpOperationException>(() => logins.Require(["native_read"], null, CancellationToken.None));
        }
    }

    private static (TestLogins Logins, FakeContinuation Continuation) Rail(bool configured = true, bool recoverable = false)
    {
        var continuation = new FakeContinuation();
        var services = new ServiceCollection().AddSingleton<IUserActionContinuation>(continuation).BuildServiceProvider();
        return (new TestLogins(configured, services, recoverable), continuation);
    }

    private static string RequestOf(UserActionRequest action)
        => new Uri(action.LoginUrl).Query["?request=".Length..];

    private static Turn EnterTurn(SpecialistRequest? specialist = null, string[]? allowedTools = null)
    {
        var context = new AgentTurnContext(new NeuronId("chat", Owner, "main"), new CommandId(Guid.NewGuid()), Actor,
            allowedTools, specialist);
        return new Turn(context, AgentTurnContext.Enter(context), VerifiedActor.Enter(Actor));
    }

    private sealed class Turn(AgentTurnContext context, IDisposable turn, IDisposable actor) : IDisposable
    {
        public AgentTurnContext Context { get; } = context;

        public void Dispose()
        {
            actor.Dispose();
            turn.Dispose();
        }
    }

    private sealed class TestLogins(bool configured, IServiceProvider services, bool recoverable = false)
        : BrowserLogins(new BrowserLoginDefinition("test", "Test", "TestScheme", "/integrations/test/login", "/integrations/test/callback", "Log in.")
        { RecoverPendingAfterRestart = recoverable, ReadinessCheckInterval = TimeSpan.Zero }, services)
    {
        protected override Uri? PublicOrigin => configured ? new Uri("http://localhost:5080") : null;
        public string? Revision { get; set; }
        public string? WaitingMessage { get; set; }
        protected override string? GetConnectionRevision(AgentTurnContext context) => Revision;
        protected override Task<string?> WaitBeforeResumeAsync(AgentTurnContext context, string? scope, CancellationToken cancellationToken)
            => Task.FromResult(WaitingMessage);
    }

    private sealed class FakeContinuation : IUserActionContinuation
    {
        public bool Waiting { get; set; } = true;

        public List<(AgentTurnContext Context, bool Accepted)> Completed { get; } = [];

        public Task CompleteAsync(AgentTurnContext context, string actionId, bool accepted, CancellationToken cancellationToken)
        {
            Completed.Add((context, accepted));
            return Task.CompletedTask;
        }

        public Task<bool> IsWaitingAsync(AgentTurnContext context, string actionId, CancellationToken cancellationToken)
            => Task.FromResult(Waiting);
    }
}
