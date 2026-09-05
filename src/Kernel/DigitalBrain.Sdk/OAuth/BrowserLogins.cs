using DigitalBrain.Product.Identity;
using System.Security.Cryptography;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Sdk;

// One pending-login registry per provider. A login request is a one-use capability minted for
// the current agent turn: the browser opens it once, the OAuth callback claims it once, the
// provider publishes credentials only while it is still active, and the durable turn is resumed
// exactly once by the delivery worker. Credentials never enter this class; it holds control data.
public abstract class BrowserLogins : IUserActionSource
{
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services;

    protected BrowserLogins(BrowserLoginDefinition definition, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(services);
        Definition = definition;
        _services = services;
    }

    public BrowserLoginDefinition Definition { get; }
    protected IServiceProvider Services => _services;

    // Null until the operator has supplied the provider's OAuth client; no login can start before.
    protected abstract Uri? PublicOrigin { get; }

    // Providers resolve this from credentials accepted for this authenticated actor.
    protected virtual string? GetConnectionRevision(AgentTurnContext context) => null;
    // A non-null message keeps an accepted login waiting for bounded setup proof.
    // Providers must not hold the callback HTTP request while waiting.
    protected virtual Task<string?> WaitBeforeResumeAsync(AgentTurnContext context, string? scope, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    internal Uri? ConfiguredOrigin => PublicOrigin;

    // Failures surface as MCP operation errors because a login only ever gates an MCP tool call.
    public UserActionRequest Require(string[] resumeToolNames, string? scope, CancellationToken cancellationToken,
        SetupContinuation? setupContinuation = null)
    {
        ArgumentNullException.ThrowIfNull(resumeToolNames);
        if (setupContinuation is not null && (setupContinuation.Scope != scope
            || resumeToolNames.Length != 1 || resumeToolNames[0] != setupContinuation.ToolName))
        {
            throw new ArgumentException("A setup continuation must match the exact scope and resumed tool.", nameof(setupContinuation));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var origin = PublicOrigin
            ?? throw new McpOperationException($"{Definition.DisplayName} setup is incomplete. Configure its OAuth client privately in Aspire.");
        var context = AgentTurnContext.Current
            ?? throw new McpOperationException($"Request {Definition.DisplayName} access from an authenticated chat.");
        if (VerifiedActor.Current?.PrincipalId != context.Actor.PrincipalId)
        {
            throw new McpOperationException("An authenticated chat actor is required.");
        }

        if (context.AllowedToolNames is not null)
        {
            throw new McpOperationException(
                $"{Definition.DisplayName} authorization did not complete this read. Send a new request after checking access; automatic login will not repeat.");
        }

        lock (_pending)
        {
            foreach (var key in _pending.Where(p => p.Value.Done && p.Value.Action.ExpiresAt <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray())
            {
                _pending.Remove(key);
            }

            var existing = _pending.Values.FirstOrDefault(p => SameTurn(p.Context, context));
            if (existing is not null)
            {
                if (existing.Done)
                {
                    throw new McpOperationException($"This {Definition.DisplayName} login request is already settled. Send a new request.");
                }

                existing.Scope ??= scope;
                existing.Action = existing.Action with { Scope = existing.Scope };
                if (resumeToolNames.Length == 0)
                {
                    existing.Action = existing.Action with { ResumeToolNames = [], SpecialistContinuation = null, SetupContinuation = null };
                }

                return existing.Action;
            }

            if (_pending.Count >= Definition.Capacity)
            {
                throw new McpOperationException($"Too many pending {Definition.DisplayName} logins. Wait for an existing request to expire.");
            }

            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var action = new UserActionRequest(
                Guid.NewGuid().ToString("N"),
                Definition.Provider,
                Definition.DisplayName,
                Definition.Message,
                new Uri(origin, $"{Definition.LoginPath}?request={id}").AbsoluteUri,
                DateTimeOffset.UtcNow.Add(Definition.Lifetime),
                [.. resumeToolNames], CreateSpecialistContinuation(context, resumeToolNames), scope, setupContinuation);
            var pending = new Pending(context, action, scope);
            _pending.Add(id, pending);
            pending.Cancellation = cancellationToken.Register(() => Cancel(context));
            cancellationToken.ThrowIfCancellationRequested();
            return action;
        }
    }

    public UserActionRequest? Find(OwnerId owner, CommandId commandId)
    {
        var context = AgentTurnContext.Current;
        lock (_pending)
        {
            return _pending.Values.FirstOrDefault(p => !p.Done && p.Context.Chat.Owner == owner
                && p.Context.CommandId == commandId && context?.Chat == p.Context.Chat && context.Actor == p.Context.Actor)?.Action;
        }
    }

    public UserActionRequest? Recover(AgentTurnContext context, UserActionRequest action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);
        if (!Definition.RecoverPendingAfterRestart || action.Provider != Definition.Provider
            || action.ResumeToolNames.Length == 0 || action.SpecialistContinuation is not null
            || context.AllowedToolNames is not null || VerifiedActor.Current != context.Actor
            || PublicOrigin is null)
        {
            return null;
        }
        lock (_pending)
        {
            // A live, consumed or cancelled capability must never be refreshed.
            // Recovery is only for a durable waiting intent absent after restart.
            if (_pending.Values.Any(p => SameTurn(p.Context, context)))
            {
                return null;
            }
            using var turn = AgentTurnContext.Enter(context);
            return Require(action.ResumeToolNames, action.Scope, CancellationToken.None, action.SetupContinuation);
        }
    }

    public SpecialistContinuation? ResolveSpecialistContinuation(AgentTurnContext context, string actionId)
    {
        lock (_pending)
        {
            var pending = _pending.Values.FirstOrDefault(p => SameTurn(p.Context, context)
                && p.Action.Id == actionId && p.Outcome == true && !p.Done
                && p.Action.ExpiresAt > DateTimeOffset.UtcNow);
            if (pending?.Action.SpecialistContinuation is not { } continuation)
            {
                return null;
            }
            using var actor = VerifiedActor.Enter(pending.Context.Actor);
            if (GetConnectionRevision(pending.Context) is not { Length: > 0 } revision)
            {
                return null;
            }
            return continuation with { ConnectionRevision = revision };
        }
    }

    private static SpecialistContinuation? CreateSpecialistContinuation(AgentTurnContext context, string[] readTools)
    {
        if (readTools.Length == 0 || context.SpecialistRequest is not { } request)
        {
            return null;
        }
        if (request.Target.Owner != context.Chat.Owner
            || !PrincipalPartition.OwnsInstance(context.Actor.PrincipalId, request.Target.Name)
            || string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 16000)
        {
            throw new McpOperationException("The specialist continuation does not belong to this authenticated chat.");
        }
        return new SpecialistContinuation(request.Target, request.Text, [.. readTools.Distinct(StringComparer.Ordinal)]);
    }

    public void Cancel(AgentTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_pending)
        {
            foreach (var p in _pending.Values.Where(p => SameTurn(p.Context, context)))
            {
                p.Done = true;
                p.Outcome = false;
                p.Cancellation.Unregister();
            }
        }
    }

    internal bool TryBegin(string? request, out string? scope)
    {
        lock (_pending)
        {
            scope = null;
            if (request is null || !_pending.TryGetValue(request, out var p) || !Active(p) || p.State != 0)
            {
                return false;
            }

            p.State = 1;
            scope = p.Scope;
            return true;
        }
    }

    internal bool TryClaim(string? request)
    {
        lock (_pending)
        {
            if (request is null || !_pending.TryGetValue(request, out var p) || !Active(p) || p.State != 1)
            {
                return false;
            }

            p.State = 2;
            return true;
        }
    }

    // The provider publishes credentials inside commitIfActive: publication and cancellation
    // share one lock, so nothing can publish after a cancelled or expired request.
    public Task AcceptAsync(string request, Func<OwnerId, string?, Action<Action>, Task> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        return AcceptForActorAsync(request, (context, scope, commit) => accept(context.Chat.Owner, scope, commit));
    }

    public async Task AcceptForActorAsync(string request, Func<AgentTurnContext, string?, Action<Action>, Task> accept)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accept);
        Pending p;
        lock (_pending)
        {
            if (!_pending.TryGetValue(request, out p!) || !Active(p) || p.State != 2)
            {
                throw new McpOperationException($"This {Definition.DisplayName} login expired or was cancelled.");
            }
        }

        var committed = false;
        await accept(p.Context, p.Scope, commit =>
        {
            lock (_pending)
            {
                if (!Active(p) || p.State != 2)
                {
                    throw new McpOperationException($"This {Definition.DisplayName} login expired or was cancelled.");
                }

                commit();
                committed = true;
            }
        }).ConfigureAwait(false);
        lock (_pending)
        {
            if (committed && Active(p) && p.State == 2)
            {
                p.Outcome = true;
            }
        }
    }

    public void Reject(string? request)
    {
        lock (_pending)
        {
            if (request is not null && _pending.TryGetValue(request, out var p) && !p.Done && p.Outcome is null)
            {
                p.Outcome = false;
            }
        }
    }

    // Idempotent: a failed delivery leaves the request pending and the worker retries it.
    public async Task DeliverAsync(CancellationToken cancellationToken)
    {
        Pending[] ready;
        lock (_pending)
        {
            foreach (var p in _pending.Values.Where(p => !p.Done && p.Action.ExpiresAt <= DateTimeOffset.UtcNow))
            {
                p.Outcome = false;
            }

            ready = _pending.Values.Where(p => !p.Done && p.Outcome is not null).ToArray();
        }

        foreach (var p in ready)
        {
            if (p.Outcome == true && p.NextReadinessCheck > DateTimeOffset.UtcNow)
            {
                continue;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var continuation = _services.GetRequiredService<IUserActionContinuation>();
            if (p.Action.ExpiresAt > DateTimeOffset.UtcNow
                && !await continuation.IsWaitingAsync(p.Context, p.Action.Id, deadline.Token).ConfigureAwait(false))
            {
                // The callback can finish before the original AI turn publishes its login card.
                // Wait for that durable state instead of losing the one-shot continuation.
                continue;
            }

            if (p.Outcome == true)
            {
                var waiting = await WaitBeforeResumeAsync(p.Context, p.Scope, deadline.Token).ConfigureAwait(false);
                if (waiting is not null)
                {
                    var changed = false;
                    lock (_pending)
                    {
                        if (!p.Done)
                        {
                            changed = p.Action.Stage != "readiness" || p.Action.Message != waiting;
                            p.Action = p.Action with { Message = waiting, Stage = "readiness" };
                            p.NextReadinessCheck = DateTimeOffset.UtcNow.Add(Definition.ReadinessCheckInterval);
                        }
                    }
                    if (changed)
                    {
                        _ = await continuation.IsWaitingAsync(p.Context, p.Action.Id, deadline.Token).ConfigureAwait(false);
                    }
                    continue;
                }
            }

            await continuation.CompleteAsync(p.Context, p.Action.Id, p.Outcome == true, deadline.Token).ConfigureAwait(false);
            lock (_pending)
            {
                p.Done = true;
                p.Cancellation.Unregister();
            }
        }
    }

    private static bool Active(Pending p) => !p.Done && p.Outcome is null && p.Action.ExpiresAt > DateTimeOffset.UtcNow;

    private static bool SameTurn(AgentTurnContext a, AgentTurnContext b)
        => a.Chat == b.Chat && a.Actor == b.Actor && a.CommandId == b.CommandId;

    private sealed class Pending(AgentTurnContext context, UserActionRequest action, string? scope)
    {
        internal readonly AgentTurnContext Context = context;
        internal UserActionRequest Action = action;
        internal string? Scope = scope;
        internal int State;
        internal bool? Outcome;
        internal bool Done;
        internal CancellationTokenRegistration Cancellation;
        internal DateTimeOffset NextReadinessCheck;
    }
}
