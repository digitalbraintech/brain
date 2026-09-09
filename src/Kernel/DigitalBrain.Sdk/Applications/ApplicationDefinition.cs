using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Chat;
using Orleans.Runtime;

namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationDefinition
{
    // This is an internal foundation for the future artifact supervisor. Author code cannot
    // install manifests or start workers until execution is bound to a server-issued artifact lease.
    private readonly IApplicationKernel _kernel;
    private readonly ActorContext _actor;
    private readonly IGrainFactory _grains;
    private readonly IApplicationCatalog _catalog;
    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BehaviorDefinition> _behaviors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApplicationScriptReference> _scripts = new(StringComparer.Ordinal);

    internal ApplicationDefinition(IApplicationKernel kernel, ActorContext actor, IGrainFactory grains)
    {
        (_kernel, _actor, _grains) = (kernel, actor, grains);
        var key = kernel.GetPrimaryKeyString().Split('/');
        _catalog = grains.GetGrain<IApplicationCatalog>($"{key[0]}/{key[1]}");
    }

    public CommandPort<TRequest, TResult> Command<TRequest, TResult>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new(_kernel, _actor, new ApplicationOperation(key, WireName(typeof(TRequest)), WireName(typeof(TResult))));
    }

    public CommandPort<TRequest, TResult> Command<TRequest, TResult>(string key,
        Func<TRequest, ApplicationExecutionContext, CancellationToken, Task<TResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        var declaration = new ApplicationOperation(key, WireName(typeof(TRequest)), WireName(typeof(TResult)));
        if (!_handlers.TryAdd(key, new(declaration, async (payload, context, ct) =>
            JsonSerializer.Serialize(await handler(JsonSerializer.Deserialize<TRequest>(payload)!, context, ct).ConfigureAwait(false)))))
        {
            throw new InvalidOperationException($"Operation '{key}' is already declared.");
        }
        return new(_kernel, _actor, declaration);
    }

    public BehaviorDefinition Behavior(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Contains('/', StringComparison.Ordinal)) { throw new ArgumentException("Behavior keys cannot contain '/'.", nameof(key)); }
        if (!_behaviors.TryGetValue(key, out var behavior))
        {
            behavior = new(this, key);
            _behaviors.Add(key, behavior);
        }
        return behavior;
    }

    public ApplicationScriptReference Script(string key, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Replace('\\', '/').Split('/').Any(part => part == ".."))
        {
            throw new ArgumentException("Child script paths must stay relative to the declaring source.", nameof(relativePath));
        }
        var reference = new ApplicationScriptReference(key, relativePath.Replace('\\', '/'));
        if (!_scripts.TryAdd(key, reference)) { throw new InvalidOperationException($"Script key '{key}' is already declared."); }
        ApplicationRunScope.RecordScript(reference);
        return reference;
    }

    internal void Implement<TNeuron>(string operationKey, string instance)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance);
        if (!_handlers.TryGetValue(operationKey, out var handler))
        {
            throw new InvalidOperationException($"Operation '{operationKey}' is not declared.");
        }
        var owner = new OwnerId(_kernel.GetPrimaryKeyString().Split('/')[0]);
        _handlers[operationKey] = handler with
        {
            Declaration = handler.Declaration with
            {
                ImplementedContract = NeuronId.For<TNeuron>(owner, instance).Type,
                ImplementedInstance = instance,
            },
        };
    }

    internal CommandPort<TRequest, TResult> StatefulCommand<TRequest, TResult>(BehaviorDefinition behavior, string key,
        Func<TRequest, ApplicationExecutionContext, CancellationToken, Task<TResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var operation = $"{behavior.Key}/{key}";
        var port = Command(operation, handler);
        _handlers[operation] = _handlers[operation] with { Behavior = behavior };
        return port;
    }

    public Task RunAsync(string[] args, CancellationToken cancellationToken = default)
        => ApplicationRunScope.RunAsync(this, args, cancellationToken);

    internal string KernelKey => _kernel.GetPrimaryKeyString().Split('/')[2];
    internal OwnerId Owner => new(_kernel.GetPrimaryKeyString().Split('/')[0]);
    internal ActorContext Actor => _actor;

    internal Task<string[]> PendingRevisionsAsync(CancellationToken cancellationToken = default)
    {
        using var actor = EnterActor();
        return _kernel.PendingRevisions().WaitAsync(cancellationToken);
    }

    public void OnUserMessage(string key, string exactText,
        Func<UserMessaged, ApplicationExecutionContext, CancellationToken, Task<string>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactText);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(exactText.Length, 4096);
        Command(key, handler);
        var registered = _handlers[key];
        var owner = new OwnerId(_kernel.GetPrimaryKeyString().Split('/')[0]);
        _handlers[key] = registered with
        {
            Declaration = registered.Declaration with
            {
                Trigger = new(new NeuronId("usermessages", owner, "inbox").ToString(), "chat.user-messaged/v1", exactText),
            },
        };
    }

    public void OnUserMessageContaining(string key, string text,
        Func<UserMessaged, ApplicationExecutionContext, CancellationToken, Task<string>> handler)
    {
        OnUserMessage(key, text, handler);
        var registered = _handlers[key];
        _handlers[key] = registered with
        {
            Declaration = registered.Declaration with
            {
                Trigger = registered.Declaration.Trigger! with { ContainsIgnoreCase = true },
            },
        };
    }

    // The installer supplies the immutable artifact revision; installation never executes handlers.
    internal async Task InstallAsync(string revision, CancellationToken cancellationToken = default, bool activate = true)
    {
        using var actor = EnterActor();
        await _kernel.Install(BuildManifest(revision), activate)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal ApplicationManifest BuildManifest(string revision)
        => new(revision, _handlers.Values.Select(x => x.Behavior is { } behavior
                ? x.Declaration with { StateScope = behavior.Key, StateSchema = behavior.Schema } : x.Declaration)
            .OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            _outputs.Values.OrderBy(x => x.BehaviorKey, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            _connections.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray());

    internal async Task ServeAsync(string revision, CancellationToken cancellationToken = default)
    {
        using var actor = EnterActor();
        using var workerCapability = ApplicationWorkerCapabilityContext.Enter(
            ApplicationRunScope.RequireWorkerCapability());
        using var servingScope = ApplicationRunScope.Enter(ApplicationRunMode.Serve, revision, KernelKey, cancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new List<Task>();
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                foreach (var completed in running.Where(x => x.IsCompleted).ToArray())
                {
                    running.Remove(completed);
                    await completed.ConfigureAwait(false);
                }
                var delivery = await _catalog.ClaimDelivery(KernelKey, revision).WaitAsync(stopping.Token).ConfigureAwait(false);
                if (delivery is not null)
                {
                    if (delivery.TargetNeuronId is { } targetValue && delivery.ObservedDelivery is { } observed)
                    {
                        var target = ParseOwnedNeuron(targetValue, Owner);
                        var outcome = await _grains.GetGrain<INeuronGrain>(target.ToGrainId())
                            .Deliver(observed, stopping.Token).WaitAsync(stopping.Token).ConfigureAwait(false);
                        SignalRequestPolicy.RequireHandled(target, observed.Signal, outcome);
                    }
                    else
                    {
                        await _kernel.SubmitPinned(delivery.OperationId, delivery.Revision, delivery.Operation,
                            delivery.Contract, "system.boolean/v1", delivery.Payload).WaitAsync(stopping.Token).ConfigureAwait(false);
                    }
                    await _catalog.AckDelivery(delivery.DeliveryId, delivery.Lease).WaitAsync(stopping.Token).ConfigureAwait(false);
                    continue;
                }
                // Admission bounds pending work. A handler awaiting a child must not
                // occupy a worker slot needed to execute that child.
                var claim = await _kernel.Claim(revision).WaitAsync(stopping.Token).ConfigureAwait(false);
                if (claim is null)
                {
                    await Task.Delay(25, stopping.Token).ConfigureAwait(false);
                    continue;
                }
                running.Add(ExecuteAsync(claim, stopping.Token));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAsync(ApplicationClaim claim, CancellationToken cancellationToken)
    {
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewing = RenewAsync(claim, executionCancellation, heartbeat.Token);
        try
        {
            if (!_handlers.TryGetValue(claim.Operation, out var handler))
            {
                throw new InvalidOperationException("The installed operation has no executable handler in this worker.");
            }
            using var executionScope = ApplicationRunScope.EnterExecution(handler.Declaration.IsQuery);
            var context = new ApplicationExecutionContext(_kernel, claim, _grains, _catalog,
                KernelKey, handler.Behavior?.Key);
            var result = await handler.Execute(claim.Payload, context, executionCancellation.Token).ConfigureAwait(false);
            await _kernel.Complete(claim.OperationId, claim.Lease, result, context.EffectCount,
                context.BranchEffectCount, context.State.PendingWrites).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            // Worker shutdown leaves accepted input for lease recovery. Explicit cancellation
            // has already recorded the durable terminal result before stopping this handler.
        }
        catch (ApplicationExecutionSuspendedException)
        {
            // The kernel atomically recorded the wait and released this execution lease.
        }
        catch (Exception error)
        {
            await _kernel.Fail(claim.OperationId, claim.Lease, error.Message).ConfigureAwait(false);
        }
        finally
        {
            await heartbeat.CancelAsync().ConfigureAwait(false);
            await renewing.ConfigureAwait(false);
        }
    }
    private async Task RenewAsync(
        ApplicationClaim claim,
        CancellationTokenSource execution,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                if (!await _kernel.Renew(claim.OperationId, claim.Lease).ConfigureAwait(false))
                {
                    await execution.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private IDisposable EnterActor()
    {
        if (VerifiedActor.Current is { } current && current.PrincipalId != _actor.PrincipalId)
        {
            throw new NeuronAuthorizationException("This application connection belongs to another principal.");
        }

        return VerifiedActor.Enter(_actor);
    }

    private static NeuronId ParseOwnedNeuron(string value, OwnerId owner)
    {
        var separator = value.IndexOf(':');
        var ownerPrefix = $"{owner.Value}/";
        if (separator <= 0 || !value.AsSpan(separator + 1).StartsWith(ownerPrefix, StringComparison.Ordinal)
            || value.Length == separator + 1 + ownerPrefix.Length)
        {
            throw new InvalidOperationException("A connection target must be a neuron owned by this brain.");
        }
        return new(value[..separator], owner, value[(separator + 1 + ownerPrefix.Length)..]);
    }

    private static string WireName(Type type) => ApplicationContracts.NameFor(type);

    private sealed record Handler(ApplicationOperation Declaration,
        Func<string, ApplicationExecutionContext, CancellationToken, Task<string>> Execute,
        BehaviorDefinition? Behavior = null);
}

public sealed class CommandPort<TRequest, TResult>
{
    private readonly IApplicationKernel _kernel;
    private readonly ActorContext _actor;
    private readonly ApplicationOperation _operation;

    internal CommandPort(IApplicationKernel kernel, ActorContext actor, ApplicationOperation operation)
        => (_kernel, _actor, _operation) = (kernel, actor, operation);

    internal string Identity => JsonSerializer.Serialize(new[] { _kernel.GetPrimaryKeyString(), _operation.Key,
        _operation.RequestContract, _operation.ResponseContract });
    internal string OperationKey => _operation.Key;
    internal string ApplicationIdentity => _kernel.GetPrimaryKeyString();

    internal Task<string> HeadAsync() => _kernel.Head();

    internal async Task<TResult> InvokePinnedAsync(TRequest request, Guid operationId, string revision, CancellationToken cancellationToken,
        ApplicationParent? parent = null)
    {
        using var actor = EnterActor();
        await _kernel.SubmitPinned(operationId, revision, _operation.Key, _operation.RequestContract,
            _operation.ResponseContract, JsonSerializer.Serialize(request), parent).WaitAsync(cancellationToken).ConfigureAwait(false);
        return await new ApplicationInvocation<TResult>(_kernel, _actor, operationId).ResultAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ApplicationInvocation<TResult>> SubmitAsync(TRequest request, CancellationToken cancellationToken = default)
        => SubmitAsync(request, Guid.NewGuid(), cancellationToken);

    public async Task<ApplicationInvocation<TResult>> SubmitAsync(TRequest request, Guid operationId, CancellationToken cancellationToken = default)
    {
        ApplicationRunScope.ThrowIfDefinitionEffect("submit application operation");
        using var actor = EnterActor();
        var id = await _kernel.Submit(operationId, _operation.Key, _operation.RequestContract, _operation.ResponseContract,
            JsonSerializer.Serialize(request)).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new(_kernel, _actor, id);
    }

    public async Task<TResult> InvokeAsync(TRequest request, CancellationToken cancellationToken = default)
        => await (await SubmitAsync(request, cancellationToken).ConfigureAwait(false)).ResultAsync(cancellationToken).ConfigureAwait(false);

    private IDisposable EnterActor()
    {
        if (VerifiedActor.Current is { } current && current.PrincipalId != _actor.PrincipalId)
        {
            throw new NeuronAuthorizationException("This application connection belongs to another principal.");
        }

        return VerifiedActor.Enter(_actor);
    }
}

public sealed class ApplicationInvocation<TResult>
{
    private readonly IApplicationKernel _kernel;
    private readonly ActorContext _actor;
    internal ApplicationInvocation(IApplicationKernel kernel, ActorContext actor, Guid id)
        => (_kernel, _actor, OperationId) = (kernel, actor, id);
    public Guid OperationId { get; }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        using var actor = EnterActor();
        await _kernel.Cancel(OperationId).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> ResultAsync(CancellationToken cancellationToken = default)
    {
        using var actor = EnterActor();
        while (true)
        {
            var result = await _kernel.Read(OperationId).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.Status == "completed") { return JsonSerializer.Deserialize<TResult>(result.Value!)!; }
            if (result.Status == "failed") { throw new InvalidOperationException($"Application operation {OperationId} failed: {result.Error}"); }
            if (result.Status == "cancelled")
            {
                throw new OperationCanceledException($"Application operation {OperationId} was cancelled.");
            }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private IDisposable EnterActor()
    {
        if (VerifiedActor.Current is { } current && current.PrincipalId != _actor.PrincipalId)
        {
            throw new NeuronAuthorizationException("This application connection belongs to another principal.");
        }

        return VerifiedActor.Enter(_actor);
    }
}
