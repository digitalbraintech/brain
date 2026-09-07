using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationExecutionContext
{
    private readonly IApplicationKernel _kernel;
    private readonly ApplicationClaim _claim;
    private readonly IGrainFactory _grains;
    private readonly IApplicationCatalog _catalog;
    private readonly string _applicationKey;
    private readonly string? _behaviorKey;
    private int _ordinal;
    private int _calling;
    private readonly HashSet<string> _branches = new(StringComparer.Ordinal);

    internal ApplicationExecutionContext(IApplicationKernel kernel, ApplicationClaim claim, IGrainFactory grains,
        IApplicationCatalog catalog, string applicationKey, string? behaviorKey)
    {
        (_kernel, _claim, _grains, _catalog, _applicationKey, _behaviorKey) =
            (kernel, claim, grains, catalog, applicationKey, behaviorKey);
        State = new(claim.StateScope, claim.StateSchema, claim.InitialState);
    }

    public Guid OperationId => _claim.OperationId;
    public string Revision => _claim.Revision;
    public ApplicationExecutionState State { get; }
    internal int EffectCount => _ordinal;

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        var owner = new OwnerId(_kernel.GetPrimaryKeyString().Split('/')[0]);
        await CheckpointAsync<bool>("initialize-owner-root", revision: null, async (_, ct) =>
        {
            await _grains.GetGrain<IBrainNeuron>(IBrainNeuron.ForOwner(owner).ToGrainId())
                .Activate().WaitAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
    internal int BranchEffectCount { get { lock (_branches) { return _branches.Count; } } }

    public ApplicationExecutionBranch Branch(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (State.PendingWrites.Count != 0)
        {
            throw new InvalidOperationException("Parallel branches cannot start with pending shared-state writes.");
        }
        lock (_branches)
        {
            if (!_branches.Add(name)) { throw new InvalidOperationException($"Branch '{name}' is already declared."); }
        }
        return new(this, name);
    }

    public async Task ApplyScriptAsync(
        ApplicationScriptReference script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        var revision = ApplicationRunScope.ChildRevision(script);
        var identity = JsonSerializer.Serialize(new[] { "apply-script", script.Key, revision });
        _ = await CheckpointAsync<bool>(identity, revision, async (_, ct) =>
        {
            var key = _kernel.GetPrimaryKeyString().Split('/');
            var child = _grains.GetGrain<IApplicationKernel>($"{key[0]}/{key[1]}/{script.Key}");
            if (await child.Declares(revision, "$apply").WaitAsync(ct).ConfigureAwait(false))
            {
                var applyId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"apply\0{revision}"))[..16]);
                await child.SubmitPinned(applyId, revision, "$apply", "system.boolean/v1", "system.boolean/v1", "true")
                    .WaitAsync(ct).ConfigureAwait(false);
                while (true)
                {
                    var applied = await child.Read(applyId).WaitAsync(ct).ConfigureAwait(false);
                    if (applied.Status == "completed") { break; }
                    if (applied.Status == "failed")
                    {
                        throw new InvalidOperationException($"Child script '{script.Key}' apply failed: {applied.Error}");
                    }
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
            }
            var initialization = await child.Activate(revision).WaitAsync(ct).ConfigureAwait(false);
            await AwaitInitializationAsync(child, initialization, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AwaitInitializationAsync(
        IApplicationKernel kernel, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            while (true)
            {
                var result = await kernel.ReadIfKnown(id).WaitAsync(cancellationToken).ConfigureAwait(false);
                if (result?.Status == "completed") { break; }
                if (result?.Status == "failed")
                {
                    throw new InvalidOperationException($"Child initialization input {id} failed: {result.Error}");
                }
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<TResult> InvokeAsync<TRequest, TResult>(CommandPort<TRequest, TResult> target,
        TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var identity = JsonSerializer.Serialize(new[] { target.Identity, JsonSerializer.Serialize(request) });
        var revision = target.ApplicationIdentity == _kernel.GetPrimaryKeyString()
            ? Revision
            : await target.HeadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return await CheckpointAsync<TResult>(identity, revision,
            (effect, ct) => target.InvokePinnedAsync(request, effect.OperationId, effect.Revision!, ct,
                new(_kernel.GetPrimaryKeyString(), OperationId)),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<TResponse> CallAsync<TNeuron, TResponse>(
        NeuronReference<TNeuron> target,
        Signal<TResponse> request,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron
        where TResponse : Signal
    {
        ArgumentNullException.ThrowIfNull(request);
        var handled = typeof(IHandle<>).MakeGenericType(request.GetType());
        if (!handled.IsAssignableFrom(typeof(TNeuron)))
        {
            throw new InvalidOperationException(
                $"Neuron '{typeof(TNeuron).Name}' does not IHandle '{request.GetType().Name}'.");
        }

        var identity = JsonSerializer.Serialize(new[]
        {
            target.Id.ToString(),
            ApplicationContracts.NameFor(request.GetType()),
            ApplicationContracts.NameFor(typeof(TResponse)),
            JsonSerializer.Serialize(request, request.GetType()),
        });
        return CheckpointAsync<TResponse>(identity, revision: null,
            (effect, ct) => CallNeuronAsync(target.Id, request, new SignalId(effect.OperationId),
                new CorrelationId(OperationId), sourceEpoch: 1, ct),
            cancellationToken);
    }

    public Task SendAsync<TNeuron>(
        NeuronReference<TNeuron> target,
        Signal request,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron
    {
        ArgumentNullException.ThrowIfNull(request);
        var handled = typeof(IHandle<>).MakeGenericType(request.GetType());
        if (!handled.IsAssignableFrom(typeof(TNeuron)))
        {
            throw new InvalidOperationException(
                $"Neuron '{typeof(TNeuron).Name}' does not IHandle '{request.GetType().Name}'.");
        }

        var identity = JsonSerializer.Serialize(new[]
        {
            target.Id.ToString(),
            ApplicationContracts.NameFor(request.GetType()),
            "completion",
            JsonSerializer.Serialize(request, request.GetType()),
        });
        return CheckpointAsync<bool>(identity, revision: null,
            async (effect, ct) =>
            {
                await SendNeuronAsync(target.Id, request, new SignalId(effect.OperationId),
                    new CorrelationId(OperationId), sourceEpoch: 1, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
    }

    internal async Task<TResponse> CallBranchAsync<TNeuron, TResponse>(string branch,
        NeuronReference<TNeuron> target, Signal<TResponse> request, CancellationToken cancellationToken)
        where TNeuron : INeuron
        where TResponse : Signal
    {
        if (State.PendingWrites.Count != 0)
        {
            throw new InvalidOperationException("Parallel branches cannot run with pending shared-state writes.");
        }
        var identity = CallIdentity(target.Id, request, typeof(TResponse));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var effect = await _kernel.BeginBranchEffect(OperationId, _claim.Lease, branch, hash, revision: null)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (effect.Result is not null) { return JsonSerializer.Deserialize<TResponse>(effect.Result)!; }
        var result = await CallNeuronAsync(target.Id, request, new SignalId(effect.OperationId),
            new CorrelationId(OperationId), 1, cancellationToken).ConfigureAwait(false);
        await _kernel.FinishBranchEffect(OperationId, _claim.Lease, branch, JsonSerializer.Serialize(result))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    internal async Task<TResult> InvokeBranchAsync<TRequest, TResult>(string branch,
        CommandPort<TRequest, TResult> target, TRequest request, CancellationToken cancellationToken)
    {
        if (State.PendingWrites.Count != 0)
        {
            throw new InvalidOperationException("Parallel branches cannot run with pending shared-state writes.");
        }
        var revision = target.ApplicationIdentity == _kernel.GetPrimaryKeyString()
            ? Revision
            : await target.HeadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var identity = JsonSerializer.Serialize(new[]
        {
            target.Identity,
            JsonSerializer.Serialize(request, typeof(TRequest)),
        });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var effect = await _kernel.BeginBranchEffect(OperationId, _claim.Lease, branch, hash, revision)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (effect.Result is not null) { return JsonSerializer.Deserialize<TResult>(effect.Result)!; }
        var result = await target.InvokePinnedAsync(request, effect.OperationId, effect.Revision!, cancellationToken,
                new(_kernel.GetPrimaryKeyString(), OperationId))
            .ConfigureAwait(false);
        await _kernel.FinishBranchEffect(OperationId, _claim.Lease, branch, JsonSerializer.Serialize(result))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<TResponse> CallNeuronAsync<TResponse>(NeuronId receiver, Signal<TResponse> request,
        SignalId signalId, CorrelationId correlation, long sourceEpoch, CancellationToken cancellationToken)
        where TResponse : Signal
    {
        var key = _kernel.GetPrimaryKeyString().Split('/');
        var principal = new PrincipalId(Guid.Parse(key[1]));
        var sourceId = new NeuronId("application-call-source", receiver.Owner,
            PrincipalPartition.InstanceName(principal, $"{key[2]}.{signalId.Value:N}"));
        var source = _grains.GetGrain<IApplicationCallSource>(sourceId.ToGrainId());
        var delivery = await source.Prepare(request, signalId, correlation, sourceEpoch)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var target = _grains.GetGrain<INeuronQuery>(receiver.ToGrainId());
        var cursor = await target.ReadJournal(JournalKind.Outgoing, long.MaxValue)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await _grains.GetGrain<INeuronGrain>(receiver.ToGrainId())
            .Deliver(delivery, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        SignalRequestPolicy.RequireHandled(receiver, request, outcome);
        var after = cursor.ResumeSequence;
        while (true)
        {
            var page = await target.ReadJournal(JournalKind.Outgoing, after)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            var retained = await SignalRequestPolicy.RecoverRetainedAsync(page,
                sequence => target.ReadJournal(JournalKind.Outgoing, sequence)
                    .WaitAsync(cancellationToken)).ConfigureAwait(false);
            if (SignalRequestPolicy.FindResponse(retained, receiver, delivery, typeof(TResponse)) is TResponse response)
            {
                return response;
            }
            after = retained.ResumeSequence;
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendNeuronAsync(NeuronId receiver, Signal request, SignalId signalId,
        CorrelationId correlation, long sourceEpoch, CancellationToken cancellationToken)
    {
        var key = _kernel.GetPrimaryKeyString().Split('/');
        var principal = new PrincipalId(Guid.Parse(key[1]));
        var sourceId = new NeuronId("application-call-source", receiver.Owner,
            PrincipalPartition.InstanceName(principal, $"{key[2]}.{signalId.Value:N}"));
        var source = _grains.GetGrain<IApplicationCallSource>(sourceId.ToGrainId());
        var delivery = await source.Prepare(request, signalId, correlation, sourceEpoch)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await _grains.GetGrain<INeuronGrain>(receiver.ToGrainId())
            .Deliver(delivery, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        SignalRequestPolicy.RequireHandled(receiver, request, outcome);
    }

    private static string CallIdentity<TResponse>(NeuronId target, Signal<TResponse> request, Type responseType)
        where TResponse : Signal
        => JsonSerializer.Serialize(new[]
        {
            target.ToString(),
            ApplicationContracts.NameFor(request.GetType()),
            ApplicationContracts.NameFor(responseType),
            JsonSerializer.Serialize(request, request.GetType()),
        });

    internal async Task<TResult> CheckpointAsync<TResult>(string identity, string? revision,
        Func<ApplicationEffect, CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken)
    {
        ApplicationRunScope.ThrowIfDefinitionEffect("execute a recorded effect");
        if (Interlocked.Exchange(ref _calling, 1) != 0)
        {
            throw new InvalidOperationException("Concurrent calls require separate execution branches.");
        }
        try
        {
            var ordinal = _ordinal++;
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            var effect = await _kernel.BeginEffect(OperationId, _claim.Lease, ordinal, hash, revision, State.PendingWrites)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            State.Checkpointed();
            if (effect.Result is not null)
            {
                return JsonSerializer.Deserialize<TResult>(effect.Result)!;
            }
            var result = await execute(effect, cancellationToken).ConfigureAwait(false);
            await _kernel.FinishEffect(OperationId, _claim.Lease, ordinal, JsonSerializer.Serialize(result))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            Volatile.Write(ref _calling, 0);
        }
    }
}
