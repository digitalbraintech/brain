using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Core;
// The script is data. This activation accepts work and commits results; user C# runs
// in the separate scripting host and cannot hold a subscription-delivery turn open.
[GrainType("behavior")]
internal sealed class BehaviorNeuron : Neuron, IBehavior, IBehaviorKernel
{
    private const int PendingCapacity = 256;
    private const int ReceiptCapacity = 100000;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);
    private readonly IDurableValue<byte[]> _storage;
    private readonly Serializer<BehaviorState> _serializer;
    private readonly Serializer<Signal> _signalSerializer;
    private readonly Serializer<BehaviorView> _viewSerializer;
    private readonly SemaphoreSlim _fenceMutation = new(1, 1);
    private Task? _flushing;
    private Task? _maintaining;
    public BehaviorNeuron(NeuronRuntime runtime) : base(runtime)
    {
        _storage = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>("behavior.state.v1");
        _serializer = ServiceProvider.GetRequiredService<Serializer<BehaviorState>>();
        _signalSerializer = ServiceProvider.GetRequiredService<Serializer<Signal>>();
        _viewSerializer = ServiceProvider.GetRequiredService<Serializer<BehaviorView>>();
    }

    private BehaviorState Load() => _storage.Value is { Length: > 0 } bytes ? _serializer.Deserialize(bytes) : new();
    private async Task Commit(BehaviorState state)
    {
        var previous = _storage.Value;
        var previousState = previous is { Length: > 0 } ? _serializer.Deserialize(previous) : new();
        var publicStateChanged = !_viewSerializer.SerializeToArray(View(previousState)).AsSpan()
            .SequenceEqual(_viewSerializer.SerializeToArray(View(state)));
        var bytes = _serializer.SerializeToArray(state);
        _storage.Value = bytes;
        try
        {
            if (publicStateChanged)
            {
                await RecordOutgoingAsync(new BehaviorStateChanged(Id)).ConfigureAwait(true);
            }
            else
            {
                await WriteStateAsync().ConfigureAwait(true);
            }
        }
        catch
        {
            if (ReferenceEquals(_storage.Value, bytes))
            {
                _storage.Value = previous;
            }

            throw;
        }
    }

    private async Task CommitOutgoing(BehaviorState state, SignalDelivery delivery)
    {
        var previous = _storage.Value;
        var bytes = _serializer.SerializeToArray(state);
        _storage.Value = bytes;
        try
        {
            await RecordOutgoingAsync(delivery).ConfigureAwait(true);
        }
        catch
        {
            if (ReferenceEquals(_storage.Value, bytes))
            {
                _storage.Value = previous;
            }

            throw;
        }
    }

    protected override Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        _ = this.RegisterGrainTimer(Recover, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15));
        return Task.CompletedTask;
    }

    public override Task<int> Broadcast(Signal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return BroadcastAsync(signal, RequireClaimedWork().Input);
    }

    public override Task<SignalDeliveryResult> SendFrom(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return SendAsync(receiver, signal, RequireClaimedWork().Input, cancellationToken);
    }

    private BehaviorWork RequireClaimedWork()
    {
        var now = TimeProvider.GetUtcNow();
        var claimed = Load().Work
            .Where(work => work.ClaimToken is not null && !work.Terminal && work.LeaseUntil > now)
            .ToArray();
        if (claimed.Length != 1)
        {
            throw new InvalidOperationException(
                "Script publish/send requires exactly one claimed behavior execution.");
        }

        return claimed[0];
    }

    public Task<BehaviorView> ReadState()
    {
        RequireActor();
        return Task.FromResult(View(Load()));
    }

    private BehaviorView View(BehaviorState state) => new(Id, state.Principal, state.Draft, state.Active, state.Enabled, state.Epoch, state.Work.Count(work => !work.Terminal), state.Detail);
    public async Task HandleAsync(SaveBehaviorScript signal, CancellationToken cancellationToken)
    {
        var principal = RequireActor();
        if (await ReplayCommand(signal).ConfigureAwait(true))
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(signal.Source);
        if (signal.Source.Length > 512 * 1024)
        {
            throw new ArgumentException("Behavior source exceeds 512 KiB.");
        }

        var state = Load();
        if (signal.ExpectedDraftRevision is { } expected && state.Draft?.Revision != expected)
        {
            throw new InvalidOperationException("The draft changed. Read it before saving another edit.");
        }

        state.Principal = principal;
        state.Draft = new(Guid.NewGuid(), signal.Source, Types(signal.InputSignalTypes), Types(signal.OutputSignalTypes), BehaviorValidation.Pending, [], TimeProvider.GetUtcNow(), signal.InputPolicy,
            SourceHash: SourceHash(signal.Source));
        state.ActivateRequested = false;
        state.Detail = "Draft saved; awaiting compilation.";
        RememberCommand(state, signal);
        await Commit(state).ConfigureAwait(true);
        await Wake().ConfigureAwait(true);
        await ReplyAsync(new BehaviorRead(View(state))).ConfigureAwait(true);
    }

    public Task HandleAsync(ReadBehavior signal, CancellationToken cancellationToken)
    {
        RequireActor();
        return ReplyAsync(new BehaviorRead(View(Load())));
    }

    public async Task HandleAsync(EnableBehavior signal, CancellationToken cancellationToken)
    {
        RequireActor();
        if (await ReplayCommand(signal).ConfigureAwait(true))
        {
            return;
        }

        var state = Load();
        if (signal.ExpectedDraftRevision is { } expected && state.Draft?.Revision != expected)
        {
            throw new InvalidOperationException("The draft changed before activation. Read the saved revision and retry.");
        }

        if (state.Draft is null)
        {
            throw new InvalidOperationException("Save a script before enabling this behavior.");
        }

        if (state.Draft.Validation == BehaviorValidation.Invalid)
        {
            throw new InvalidOperationException("The saved draft has compilation diagnostics.");
        }

        state.ActivateRequested = true;
        if (state.Draft.Validation == BehaviorValidation.Valid)
        {
            if (IncompatibleSubscription(state) is { } incompatible)
            {
                state.ActivateRequested = false;
                state.Detail = IncompatibleSubscriptionMessage(incompatible);
                await Commit(state).ConfigureAwait(true);
                throw new InvalidOperationException(state.Detail);
            }
            Activate(state);
        }
        else
        {
            state.Detail = "Waiting for draft compilation before activation.";
        }

        RememberCommand(state, signal);
        await Commit(state).ConfigureAwait(true);
        if (state.Enabled)
        {
            await ApplySubscriptions().ConfigureAwait(true);
        }

        await Wake().ConfigureAwait(true);
        await ReplyAsync(new BehaviorRead(View(state))).ConfigureAwait(true);
    }

    public async Task HandleAsync(DisableBehavior signal, CancellationToken cancellationToken)
    {
        RequireActor();
        if (await ReplayCommand(signal).ConfigureAwait(true))
        {
            return;
        }

        var state = Load();
        Fence(state);
        state.Enabled = false;
        state.ActivateRequested = false;
        state.SubscriptionsPending = true;
        state.Detail = "Disabled.";
        RememberCommand(state, signal);
        await Commit(state).ConfigureAwait(true);
        await ApplyFences().ConfigureAwait(true);
        await ApplySubscriptions().ConfigureAwait(true);
        await Wake().ConfigureAwait(true);
        await ReplyAsync(new BehaviorRead(View(Load()))).ConfigureAwait(true);
    }

    public async Task HandleAsync(InvokeBehavior signal, CancellationToken cancellationToken)
    {
        RequireActor();
        if (await ReplayCommand(signal).ConfigureAwait(true))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(signal.Input);
        var inputId = CurrentDelivery is { } command
            ? new SignalId(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"invoke:{Id}:{command.SignalId}")).AsSpan(0, 16))) : (SignalId?)null;
        await Accept(CreateDelivery(signal.Input, signalId: inputId), requireSubscription: false).ConfigureAwait(true);
        var state = Load();
        RememberCommand(state, signal);
        await Commit(state).ConfigureAwait(true);
        await ReplyAsync(new BehaviorRead(View(state))).ConfigureAwait(true);
    }

    private string CommandHash(Signal signal) => Convert.ToHexString(SHA256.HashData(_signalSerializer.SerializeToArray(signal)));
    private static string SourceHash(string source) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private async Task<bool> ReplayCommand(Signal signal)
    {
        if (CurrentDelivery is not { } delivery || !Load().Commands.TryGetValue(delivery.SignalId, out var result))
        {
            return false;
        }

        if (result.Hash != CommandHash(signal))
        {
            throw new InvalidOperationException("A behavior command delivery cannot change its payload.");
        }

        if (signal is DisableBehavior)
        {
            await ApplyFences().ConfigureAwait(true);
            await ApplySubscriptions().ConfigureAwait(true);
        }

        await ReplyAsync(new BehaviorRead(result.View)).ConfigureAwait(true);
        return true;
    }

    private void RememberCommand(BehaviorState state, Signal signal)
    {
        if (CurrentDelivery is not { } delivery)
        {
            return;
        }

        if (state.Commands.Count >= ReceiptCapacity)
        {
            throw new InvalidOperationException("The retained command ledger is full.");
        }

        state.Commands[delivery.SignalId] = new(CommandHash(signal), View(state));
    }

    protected override bool CanHandle(string signalType)
    {
        var state = Load();
        if (base.CanHandle(signalType))
        {
            return true;
        }

        var draft = state.Draft is { Validation: BehaviorValidation.Valid } valid
            ? valid.InputSignalTypes
            : [];
        var active = state.Active?.InputSignalTypes ?? [];
        return draft.Contains(signalType, StringComparer.Ordinal)
            || active.Contains(signalType, StringComparer.Ordinal);
    }

    protected override async Task<DeliveryOutcome> HandleUnmatchedAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        await Accept(delivery, requireSubscription: true).ConfigureAwait(true);
        return DeliveryOutcome.Handled;
    }

    protected override async Task ValidateSubscriptionAsync(NeuronId source, string signalType, bool subscribed)
    {
        var principal = RequireActor();
        if (source.Owner != Id.Owner || !PrincipalPartition.OwnsInstance(principal, source.Name))
        {
            throw new NeuronAuthorizationException("Behavior subscriptions must use this principal's source instances.");
        }
        var state = Load();
        if (subscribed && (state.Draft is null || state.Draft.Validation != BehaviorValidation.Valid))
        {
            throw new InvalidOperationException("Compile a valid saved script before subscribing this behavior.");
        }

        var subscription = new BehaviorSubscription(source, signalType);
        if (subscribed)
        {
            if (!state.Subscriptions.Contains(subscription))
            {
                state.Subscriptions.Add(subscription);
            }
        }
        else
        {
            state.Subscriptions.Remove(subscription);
        }
        QueueSubscription(state, subscription, subscribed);
        await Commit(state).ConfigureAwait(true);
    }

    protected override async Task OnSubscriptionChangedAsync(NeuronId source, string signalType, bool subscribed)
    {
        var state = Load();
        var subscription = new BehaviorSubscription(source, signalType);
        state.SubscriptionIntents.RemoveAll(intent => intent.Subscription == subscription && intent.Subscribed == subscribed);

        await Commit(state).ConfigureAwait(true);
        await Wake().ConfigureAwait(true);
    }

    private async Task Accept(SignalDelivery delivery, bool requireSubscription)
    {
        var principal = RequireActor();
        var state = Load();
        if (!state.Enabled || state.Active is null)
        {
            return;
        }

        if (delivery.Caller.Owner != Id.Owner || delivery.Principal != principal || !state.Active.InputSignalTypes.Contains(delivery.Signal.GetType().Name, StringComparer.Ordinal))
        {
            throw new NeuronAuthorizationException("This input is not authorized for the behavior.");
        }

        if (requireSubscription)
        {
            var edges = await GrainFactory.GetGrain<INeuronQuery>(delivery.Caller.ToGrainId()).ReadSynapses().ConfigureAwait(true);
            if (!edges.Any(edge => edge.Target == Id && edge.Kind == SynapseKind.Bound && (edge.SignalType == delivery.Signal.GetType().Name || edge.SignalType == "*")))
            {
                throw new NeuronAuthorizationException("The behavior has no subscription to this source signal.");
            }

            state = Load();
            if (!state.Enabled || state.Active is null)
            {
                return;
            }
        }

        var key = $"{delivery.Caller}:{delivery.SignalId}";
        if (state.Accepted.Contains(key))
        {
            return;
        }

        if (state.Accepted.Count >= ReceiptCapacity)
        {
            throw new InvalidOperationException("The retained receipt ledger is full.");
        }

        string? subjectKey = null;
        long? generation = null;
        string? completionKey = null;
        if (state.Active.InputPolicy != BehaviorInputPolicy.EveryEvent)
        {
            if (delivery.Signal is not IVersionedSignal versioned)
            {
                throw new InvalidOperationException("This input policy requires versioned source facts.");
            }

            if (state.Active.InputPolicy.HasFlag(BehaviorInputPolicy.ObserveFromActivation) && (versioned.CreatedAt is null || versioned.CreatedAt < state.EnabledAt))
            {
                return;
            }

            subjectKey = $"{delivery.Caller}:{versioned.SubjectKey}";
            if (!state.Subjects.TryGetValue(subjectKey, out var subject))
            {
                if (state.Subjects.Count >= ReceiptCapacity)
                {
                    throw new InvalidOperationException("The retained subject ledger is full.");
                }

                subject = new BehaviorSubject();
                state.Subjects.Add(subjectKey, subject);
            }

            if (delivery.Timestamp < subject.ObservedAt)
            {
                return;
            }

            if (subject.Version != versioned.Version)
            {
                subject.Version = versioned.Version;
                if (state.Active.InputPolicy.HasFlag(BehaviorInputPolicy.LatestPerSubject))
                {
                    subject.Generation++;
                    subject.FencePending = subject.Targets.Count > 0;
                    RecordCancellations(state, state.Work.Where(work => work.SubjectKey == subjectKey), "Replaced by a newer source version.");
                    state.Work.RemoveAll(work => work.SubjectKey == subjectKey);
                    state.Outbox.RemoveAll(output => output.SubjectKey == subjectKey);
                }
            }

            subject.ObservedAt = delivery.Timestamp;
            generation = subject.Generation;
            if (state.Active.InputPolicy.HasFlag(BehaviorInputPolicy.OncePerVersion))
            {
                completionKey = versioned.CompletionKey;
                if (subject.Completed.Contains(completionKey))
                {
                    state.Accepted.Add(key);
                    await Commit(state).ConfigureAwait(true);
                    StartMaintenance();
                    return;
                }
            }
        }

        if (state.Work.Count(work => !work.Terminal) >= PendingCapacity || state.Accepted.Count >= ReceiptCapacity)
        {
            throw new InvalidOperationException("The durable behavior inbox is full; accepted work has not been discarded.");
        }

        var workId = Guid.NewGuid();
        if (subjectKey is null)
        {
            subjectKey = $"input:{workId:N}";
            generation = 0;
            state.Subjects.Add(subjectKey, new());
        }
        var acceptedSubject = state.Subjects[subjectKey];
        var authority = BehaviorInputAuthority.From(delivery);
        if (acceptedSubject.Authority != authority)
        {
            acceptedSubject.Authority = authority;
            acceptedSubject.FencedSourceEpoch = null;
            acceptedSubject.FencedSourceGeneration = null;
        }
        state.Accepted.Add(key);
        state.Work.Add(new() { Id = workId, Epoch = state.Epoch, Input = delivery, Program = state.Active, SubjectKey = subjectKey, SubjectGeneration = generation, CompletionKey = completionKey });
        await Commit(state).ConfigureAwait(true);
        StartMaintenance();
        await Wake().ConfigureAwait(true);
    }

    public async Task ValidateDraft(Guid revision, string[] diagnostics, string[]? inputSignalTypes = null, string[]? outputSignalTypes = null, string? runtimeFingerprint = null)
    {
        RequireActor();
        var state = Load();
        var program = state.Draft?.Revision == revision ? state.Draft : state.Active?.Revision == revision ? state.Active : null;
        if (program is null)
        {
            return;
        }

        var inputs = inputSignalTypes ?? program.InputSignalTypes;
        if (diagnostics.Length == 0 && inputs.Length == 0)
        {
            diagnostics = ["Declare at least one input signal type before activation."];
        }

        var validated = program with
        {
            Validation = diagnostics.Length == 0 ? BehaviorValidation.Valid : BehaviorValidation.Invalid,
            Diagnostics = diagnostics.Take(100).ToArray(),
            InputSignalTypes = inputs,
            OutputSignalTypes = outputSignalTypes ?? program.OutputSignalTypes,
            RuntimeFingerprint = runtimeFingerprint,
            SourceHash = SourceHash(program.Source),
        };
        if (state.Draft?.Revision == revision)
        {
            state.Draft = validated;
        }
        if (state.Active?.Revision == revision)
        {
            state.Active = validated;
            if (diagnostics.Length > 0)
            {
                Fence(state);
                state.Enabled = false;
                state.SubscriptionsPending = true;
            }
        }
        state.Detail = diagnostics.Length == 0 ? "Compiled revision ready." : "Saved revision compilation failed.";
        if (state.ActivateRequested && diagnostics.Length == 0 && state.Draft?.Revision == revision)
        {
            if (IncompatibleSubscription(state) is { } incompatible)
            {
                state.ActivateRequested = false;
                state.Detail = IncompatibleSubscriptionMessage(incompatible);
            }
            else
            {
                Activate(state);
            }
        }

        await Commit(state).ConfigureAwait(true);
        if (state.Enabled || state.SubscriptionsPending)
        {
            await ApplySubscriptions().ConfigureAwait(true);
        }
        StartMaintenance();
    }

    public async Task<BehaviorClaim?> TryClaim()
    {
        RequireActor();
        var state = Load();
        if (!state.Enabled || state.Active is null)
        {
            return null;
        }

        var now = TimeProvider.GetUtcNow();
        var busySubjects = state.Work.Where(work => !work.Terminal && work.ClaimToken is not null && work.LeaseUntil > now && work.SubjectKey is not null)
            .Select(work => work.SubjectKey).ToHashSet(StringComparer.Ordinal);
        var next = state.Work.FirstOrDefault(work => !work.Terminal && work.Epoch == state.Epoch && IsDeliveryCurrent(work.Input) && work.RetryAfter <= now
            && (work.ClaimToken is null || work.LeaseUntil <= now) && state.PendingFences.Count == 0
            && (work.SubjectKey is null || !busySubjects.Contains(work.SubjectKey) && !state.Subjects[work.SubjectKey].FencePending));
        if (next is null)
        {
            return null;
        }

        if (next.Attempts >= 3)
        {
            next.Terminal = true;
            next.Detail = "Retry budget exhausted.";
            state.Detail = next.Detail;
            await Commit(state).ConfigureAwait(true);
            return null;
        }

        next.ClaimToken = Guid.NewGuid();
        next.LeaseUntil = now + Lease;
        next.Attempts++;
        state.Detail = "Processing input.";
        await Commit(state).ConfigureAwait(true);
        return new(next.Id, next.ClaimToken.Value, state.Epoch, next.Program, next.Input, state.Principal, next.Attempts, next.SubjectKey, next.SubjectGeneration);
    }

    public async Task<bool> Renew(BehaviorClaim claim)
    {
        RequireActor();
        var state = Load();
        var work = Current(state, claim);
        if (work is null)
        {
            return false;
        }

        work.LeaseUntil = TimeProvider.GetUtcNow() + Lease;
        await Commit(state).ConfigureAwait(true);
        return true;
    }

    public Task<BehaviorCheckpoint?> ReadCheckpoint(BehaviorClaim claim, string key, string requestHash)
    {
        RequireActor();
        var work = Current(Load(), claim) ?? throw new OperationCanceledException("The behavior claim is no longer current.");
        work.Checkpoints.TryGetValue(key, out var checkpoint);
        if (checkpoint is not null && checkpoint.RequestHash != requestHash)
        {
            throw new InvalidOperationException("The request at this replay position changed.");
        }

        return Task.FromResult(checkpoint);
    }

    public async Task StoreCheckpoint(BehaviorClaim claim, BehaviorCheckpoint checkpoint)
    {
        RequireActor();
        var state = Load();
        var work = Current(state, claim) ?? throw new OperationCanceledException("The behavior claim is no longer current.");
        if (work.Checkpoints.TryGetValue(checkpoint.Key, out var prior))
        {
            if (prior.RequestHash != checkpoint.RequestHash)
            {
                throw new InvalidOperationException("A request checkpoint cannot change identity.");
            }

            return;
        }

        if (work.Checkpoints.Count >= 128)
        {
            throw new InvalidOperationException("The behavior exceeded 128 durable request checkpoints.");
        }

        work.Checkpoints.Add(checkpoint.Key, checkpoint);
        await Commit(state).ConfigureAwait(true);
    }

    public async Task<SignalDelivery> PrepareRequest(BehaviorClaim claim, string key, string requestHash, NeuronId receiver, Signal request)
    {
        RequireActor();
        if (receiver.Owner != Id.Owner || receiver == Id)
        {
            throw new NeuronAuthorizationException("Behavior requests must target a different neuron owned by the same owner.");
        }

        var state = Load();
        var work = Current(state, claim) ?? throw new OperationCanceledException("The behavior claim is no longer current.");
        if (work.Requests.TryGetValue(key, out var prior))
        {
            if (prior.Hash != requestHash || prior.Receiver != receiver)
            {
                throw new InvalidOperationException("The request at this replay position changed.");
            }

            return prior.Delivery;
        }

        if (work.Requests.Count >= 128)
        {
            throw new InvalidOperationException("A behavior supports at most 128 requests per input.");
        }

        var delivery = CreateDelivery(request, work.Input, sourceEpoch: claim.Epoch, sourceStream: work.SubjectKey, streamGeneration: work.SubjectGeneration);
        work.Requests.Add(key, new(requestHash, receiver, delivery));
        state.OutputTargets.Add(receiver);
        if (work.SubjectKey is { } requestSubject)
        {
            state.Subjects[requestSubject].Targets.Add(receiver);
        }

        await CommitOutgoing(state, delivery).ConfigureAwait(true);
        return delivery;
    }

    public async Task Complete(BehaviorClaim claim, Signal? output)
    {
        RequireActor();
        var state = Load();
        var work = Current(state, claim);
        if (work is null)
        {
            return;
        }

        if (output is not null)
        {
            if (!claim.Program.OutputSignalTypes.Contains(output.GetType().Name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("The script returned an undeclared output signal.");
            }

            SignalId? outputId = null;
            if (work.SubjectKey is { } subjectKey && work.CompletionKey is { } completionKey)
            {
                var subject = state.Subjects[subjectKey];
                if (subject.Completed.Contains(completionKey))
                {
                    state.Work.Remove(work);
                    await Commit(state).ConfigureAwait(true);
                    return;
                }

                if (subject.FrozenOutputs.TryGetValue(completionKey, out var frozen))
                {
                    output = frozen;
                }
                else
                {
                    subject.FrozenOutputs.Add(completionKey, output);
                }

                outputId = new SignalId(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}:{subjectKey}:{completionKey}")).AsSpan(0, 16)));
                if (state.Outbox.Any(pending => pending.Delivery.SignalId == outputId))
                {
                    state.Work.Remove(work);
                    await Commit(state).ConfigureAwait(true);
                    StartFlush();
                    return;
                }
            }

            var delivery = CreateDelivery(output, work.Input, signalId: outputId, sourceEpoch: claim.Epoch, sourceStream: work.SubjectKey, streamGeneration: work.SubjectGeneration);
            var recipients = BroadcastRecipients(output).ToArray();
            state.OutputTargets.UnionWith(recipients);
            if (work.SubjectKey is { } outputSubject)
            {
                state.Subjects[outputSubject].Targets.UnionWith(recipients);
            }

            state.Outbox.Add(new() { Epoch = state.Epoch, Delivery = delivery, Recipients = recipients, SubjectKey = work.SubjectKey, SubjectGeneration = work.SubjectGeneration, CompletionKey = work.CompletionKey, Input = work.Input });
        }

        state.Work.Remove(work); // compact payloads; Accepted retains receipt tombstones
        state.Detail = output is null ? "Input completed without output." : "Output delivery pending.";
        await Commit(state).ConfigureAwait(true);
        StartFlush();
    }

    public async Task Fail(BehaviorClaim claim, string detail, bool retryable)
    {
        RequireActor();
        var state = Load();
        var work = Current(state, claim);
        if (work is null)
        {
            return;
        }

        work.ClaimToken = null;
        work.Terminal = !retryable || work.Attempts >= 3;
        work.Detail = detail.Length > 2048 ? detail[..2048] : detail;
        work.RetryAfter = TimeProvider.GetUtcNow().AddSeconds(Math.Min(60, 5 * work.Attempts));
        state.Detail = work.Detail;
        await Commit(state).ConfigureAwait(true);
    }

    private BehaviorWork? Current(BehaviorState state, BehaviorClaim claim) => state.Enabled && state.Epoch == claim.Epoch ? state.Work.FirstOrDefault(work => work.Id == claim.WorkId && work.Program.Revision == claim.Program.Revision && !work.Terminal && IsDeliveryCurrent(work.Input) && work.ClaimToken == claim.Token && work.LeaseUntil > TimeProvider.GetUtcNow() && (work.SubjectKey is null || state.Subjects[work.SubjectKey].Generation == work.SubjectGeneration)) : null;

    protected override Task OnSourceEpochFencedAsync(NeuronId source, long minimumEpoch)
        => InvalidateAuthority(authority => authority.Source == source && authority.Epoch is { } epoch && epoch < minimumEpoch,
            subject => subject.FencedSourceEpoch < minimumEpoch || subject.FencedSourceEpoch is null,
            subject => subject.FencedSourceEpoch = minimumEpoch);

    protected override Task OnSourceStreamFencedAsync(NeuronId source, string stream, long minimumGeneration)
        => InvalidateAuthority(authority => authority.Source == source && authority.Stream == stream
                && authority.Generation is { } generation && generation < minimumGeneration,
            subject => subject.FencedSourceGeneration < minimumGeneration || subject.FencedSourceGeneration is null,
            subject => subject.FencedSourceGeneration = minimumGeneration);

    private async Task InvalidateAuthority(Func<BehaviorInputAuthority, bool> matches,
        Func<BehaviorSubject, bool> needsAdvance, Action<BehaviorSubject> advance)
    {
        string[] streams;
        await _fenceMutation.WaitAsync().ConfigureAwait(true);
        try
        {
            var state = Load();
            bool IsOlder(SignalDelivery? input) => input is not null && matches(BehaviorInputAuthority.From(input));
            var work = state.Work.Where(item => IsOlder(item.Input)).ToArray();
            var output = state.Outbox.Where(item => IsOlder(item.Input)).ToArray();
            streams = state.Subjects.Where(pair => pair.Value.Authority is { } authority && matches(authority))
                .Select(pair => pair.Key).Concat(work.Select(item => item.SubjectKey)).Concat(output.Select(item => item.SubjectKey))
                .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            var changed = work.Length > 0 || output.Length > 0;
            foreach (var stream in streams)
            {
                if (state.Subjects.TryGetValue(stream, out var subject) && needsAdvance(subject))
                {
                    subject.Generation++;
                    subject.FencePending = subject.Targets.Count > 0;
                    // Keep original provenance for independent closure traversals;
                    // these separate floors make generation changes idempotent.
                    advance(subject);
                    changed = true;
                }
            }
            RecordCancellations(state, work, "The source authority changed before this input completed.");
            state.Work.RemoveAll(item => IsOlder(item.Input));
            state.Outbox.RemoveAll(item => IsOlder(item.Input));
            if (changed)
            {
                state.Detail = "Cancelled inputs from the previous source authority.";
                await Commit(state).ConfigureAwait(true);
            }
        }
        finally
        {
            _fenceMutation.Release();
        }
        // Traverse retained targets even if another closure already acknowledged
        // this floor. Every top-level acknowledgement proves its own full closure.
        await ApplySubjectFences(streams).ConfigureAwait(true);
    }
    private static void Fence(BehaviorState state)
    {
        state.Epoch++;
        state.PendingFences.UnionWith(state.OutputTargets);
        RecordCancellations(state, state.Work, "Behavior disabled.");
        state.Work.Clear();
        state.Outbox.Clear();
    }

    public async Task CompleteRequest(BehaviorClaim claim, string key, string requestHash)
    {
        RequireActor();
        var state = Load();
        var work = Current(state, claim) ?? throw new OperationCanceledException("The behavior claim is no longer current.");
        if (!work.Requests.TryGetValue(key, out var request) || request.Hash != requestHash)
        {
            throw new InvalidOperationException("The completed request does not match a prepared request.");
        }
        if (request.Acknowledged)
        {
            return;
        }
        work.Requests[key] = request with { Acknowledged = true };
        ReinforceDelivery(request.Receiver, request.Delivery.Signal.GetType().Name);
        await Commit(state).ConfigureAwait(true);
    }

    private static void RecordCancellations(BehaviorState state, IEnumerable<BehaviorWork> work, string reason)
    {
        state.Cancellations.AddRange(work.Select(item => new BehaviorCancellation(item.Id, item.Program.Revision, reason)));
        if (state.Cancellations.Count > PendingCapacity)
        {
            state.Cancellations.RemoveRange(0, state.Cancellations.Count - PendingCapacity);
        }
    }

    private void Activate(BehaviorState state)
    {
        if (IncompatibleSubscription(state) is { } incompatible)
        {
            throw new InvalidOperationException(IncompatibleSubscriptionMessage(incompatible));
        }
        if (state.Enabled && state.Active?.Revision == state.Draft?.Revision)
        {
            return;
        }

        state.Active = state.Draft;
        if (!state.Enabled)
        {
            state.EnabledAt = TimeProvider.GetUtcNow();
        }
        state.Enabled = true;
        state.ActivateRequested = false;
        state.SubscriptionsPending = true;
        state.Detail = "Enabled; execution follows its source subscriptions.";
    }

    private static BehaviorSubscription? IncompatibleSubscription(BehaviorState state) => state.Subscriptions
        .FirstOrDefault(subscription => state.Draft is not null
            && !state.Draft.InputSignalTypes.Contains(subscription.SignalType, StringComparer.Ordinal));

    private static string IncompatibleSubscriptionMessage(BehaviorSubscription subscription) =>
        $"Unsubscribe {subscription.SignalType} from {subscription.Source} before activating a revision that no longer accepts it.";

    private PrincipalId RequireActor()
    {
        var principal = VerifiedActor.Current?.PrincipalId ?? throw new NeuronAuthorizationException("An authenticated behavior principal is required.");
        var state = Load();
        if (!PrincipalPartition.OwnsInstance(principal, Id.Name) || state.Principal is { } owner && owner != principal)
        {
            throw new NeuronAuthorizationException("The behavior belongs to another principal.");
        }

        return principal;
    }

    private static string[] Types(string[]? types)
    {
        if (types is null)
        {
            return[];
        }

        if (types.Length > 64 || types.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Declare at most 64 nonempty signal names.");
        }

        return types.Distinct(StringComparer.Ordinal).ToArray();
    }

    private Task Wake() => GrainFactory.GetGrain<IBehaviorsKernel>(NeuronId.For<IBehaviors>(Id.Owner, "default").ToGrainId()).RegisterBehavior(Id);
    private Task Recover(CancellationToken cancellationToken)
    {
        StartMaintenance();
        return Task.CompletedTask;
    }

    private void StartMaintenance()
    {
        if (_maintaining is null || _maintaining.IsCompleted)
        {
            _maintaining = Maintain();
        }
    }

    private async Task Maintain()
    {
        var state = Load();
        if (state.Principal is not { } principal)
        {
            return;
        }

        using var actor = VerifiedActor.Enter(new ActorContext(principal, "_behavior"));
        if (state.PendingFences.Count > 0)
        {
            try
            {
                await ApplyFences().ConfigureAwait(true);
            }
            catch (Exception)
            { /* Receiver fences remain pending until acknowledged. */
            }
        }

        if (state.SubscriptionsPending || state.SubscriptionIntents.Count > 0)
        {
            try
            {
                await ApplySubscriptions().ConfigureAwait(true);
            }
            catch (Exception)
            { /* Pending binding changes remain durable. */
            }
        }

        try
        {
            await ApplySubjectFences().ConfigureAwait(true);
        }
        catch (Exception)
        { /* Old subject attempts remain fenced locally while receivers recover. */
        }

        StartFlush();
    }

    private void StartFlush()
    {
        if (_flushing is null || _flushing.IsCompleted)
        {
            _flushing = Flush();
        }
    }

    private async Task Flush()
    {
        try
        {
            var state = Load();
            if (state.Principal is not { } principal)
            {
                return;
            }

            using var actor = VerifiedActor.Enter(new ActorContext(principal, "_behavior"));
            using var capacity = new SemaphoreSlim(8);
            var attempts = new List<Task>();
            foreach (var pending in state.Outbox.ToArray())
            {
                state = Load();
                var output = state.Outbox.FirstOrDefault(item => SameOutput(item, pending));
                if (!state.Enabled || output is null || output.Epoch != state.Epoch || output.Input is { } input && !IsDeliveryCurrent(input))
                {
                    continue;
                }

                if (!output.Recorded)
                {
                    output.Recorded = true;
                    await CommitOutgoing(state, output.Delivery).ConfigureAwait(true);
                }

                foreach (var target in output.Recipients)
                {
                    attempts.Add(FlushRecipient(output, target, capacity));
                }

                if (output.Recipients.Length == 0)
                {
                    state = Load();
                    var current = state.Outbox.FirstOrDefault(item => SameOutput(item, output));
                    if (current is not null)
                    {
                        MarkPublished(state, current);
                        state.Outbox.Remove(current);
                        await Commit(state).ConfigureAwait(true);
                    }
                }
            }

            await Task.WhenAll(attempts).ConfigureAwait(true);
        }
        catch (Exception)
        { /* Durable outbox retries after reconnect or activation recovery. */
        }
    }

    private async Task FlushRecipient(BehaviorOutput output, NeuronId target, SemaphoreSlim capacity)
    {
        await capacity.WaitAsync().ConfigureAwait(true);
        try
        {
            var state = Load();
            var current = state.Outbox.FirstOrDefault(item => SameOutput(item, output));
            if (!state.Enabled || current is null || current.Epoch != state.Epoch || current.Acknowledged.Contains(target)
                || current.Input is { } input && !IsDeliveryCurrent(input))
            {
                return;
            }

            if (BroadcastRecipients(current.Delivery.Signal).Contains(target))
            {
                var handled = await GrainFactory.GetGrain<INeuronGrain>(target.ToGrainId()).Deliver(current.Delivery)
                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                if (handled != DeliveryOutcome.Handled)
                {
                    throw new InvalidOperationException("The output recipient did not handle the declared signal.");
                }
            }

            state = Load();
            current = state.Outbox.FirstOrDefault(item => SameOutput(item, output));
            if (current is null)
            {
                return;
            }

            current.Acknowledged.Add(target);
            if (current.Acknowledged.Count == current.Recipients.Length)
            {
                MarkPublished(state, current);
                state.Outbox.Remove(current);
                state.Detail = state.Outbox.Count == 0 ? "Output delivered." : "Output delivery pending.";
            }

            await Commit(state).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Each recipient retains its own pending acknowledgement. A broken or
            // slow destination cannot prevent independent destinations from running.
            try
            {
                var state = Load();
                if (state.Outbox.Any(item => SameOutput(item, output)))
                {
                    state.Detail = $"Output delivery pending to {target}: {exception.Message}";
                    if (state.Detail.Length > 2048)
                    {
                        state.Detail = state.Detail[..2048];
                    }
                    await Commit(state).ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                // The pending delivery already remains durable if persistence is unavailable.
            }
        }
        finally
        {
            capacity.Release();
        }
    }

    private async Task ApplySubscriptions()
    {
        var state = Load();
        if (state.SubscriptionsPending)
        {
            foreach (var subscription in state.Subscriptions)
            {
                QueueSubscription(state, subscription, state.Enabled);
            }
            state.SubscriptionsPending = false;
            await Commit(state).ConfigureAwait(true);
        }

        foreach (var intent in state.SubscriptionIntents.ToArray())
        {
            if (!Load().SubscriptionIntents.Any(current => current.Subscription == intent.Subscription && current.Version == intent.Version))
            {
                continue;
            }

            // This is a recovery intent, not a second graph. The source remains
            // authoritative for the actual Bound edge and receives idempotent edits.
            var source = GrainFactory.GetGrain<INeuronGrain>(intent.Subscription.Source.ToGrainId());
            await (intent.Subscribed ? source.BindOutgoing(Id, intent.Subscription.SignalType)
                : source.UnbindOutgoing(Id, intent.Subscription.SignalType)).ConfigureAwait(true);
            state = Load();
            state.SubscriptionIntents.RemoveAll(current => current.Subscription == intent.Subscription && current.Version == intent.Version);
            await Commit(state).ConfigureAwait(true);
        }
    }

    private static void QueueSubscription(BehaviorState state, BehaviorSubscription subscription, bool subscribed)
    {
        state.SubscriptionIntents.RemoveAll(intent => intent.Subscription == subscription);
        state.SubscriptionIntents.Add(new(subscription, subscribed, ++state.SubscriptionVersion));
    }

    private async Task ApplyFences()
    {
        using var reentrancy = new FenceReentrancyScope();
        var state = Load();
        var epoch = state.Epoch;
        foreach (var target in state.PendingFences.ToArray())
        {
            await (target == Id ? FenceSourceEpoch(Id, epoch)
                : GrainFactory.GetGrain<INeuronGrain>(target.ToGrainId()).FenceSourceEpoch(Id, epoch))
                .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
            await _fenceMutation.WaitAsync().ConfigureAwait(true);
            try
            {
                state = Load();
                if (state.Epoch != epoch)
                {
                    return;
                }
                state.PendingFences.Remove(target);
                await Commit(state).ConfigureAwait(true);
            }
            finally
            {
                _fenceMutation.Release();
            }
        }
    }

    private static void MarkPublished(BehaviorState state, BehaviorOutput output)
    {
        state.Detail = state.Outbox.Count <= 1 ? "Output delivered." : "Output delivery pending.";
        if (output.SubjectKey is { } key && output.CompletionKey is { } completion && state.Subjects.TryGetValue(key, out var subject))
        {
            subject.Completed.Add(completion);
            subject.FrozenOutputs.Remove(completion);
        }
    }

    private static bool SameOutput(BehaviorOutput left, BehaviorOutput right) =>
        left.Delivery.SignalId == right.Delivery.SignalId && left.Epoch == right.Epoch
        && left.SubjectKey == right.SubjectKey && left.SubjectGeneration == right.SubjectGeneration;

    private async Task ApplySubjectFences(string[]? requiredStreams = null)
    {
        // Every independent caller walks its own full closure. Shared fence-only
        // reentry admits simultaneous cyclic roots; immutable ancestor paths skip
        // only this closure's ancestors, never another caller's unfinished work.
        using var reentrancy = new FenceReentrancyScope();
        const string pathKey = "db.behavior.fence-path";
        var previousPath = Orleans.Runtime.RequestContext.Get(pathKey);
        var ancestors = previousPath as string[] ?? [];
        foreach (var pair in Load().Subjects.Where(pair => pair.Value.FencePending
            || requiredStreams?.Contains(pair.Key, StringComparer.Ordinal) == true).ToArray())
        {
            var step = $"{Id}|{pair.Key.Length}:{pair.Key}|{pair.Value.Generation}";
            if (ancestors.Contains(step, StringComparer.Ordinal))
            {
                continue;
            }
            Orleans.Runtime.RequestContext.Set(pathKey, ancestors.Append(step).ToArray());
            try
            {
                foreach (var target in pair.Value.Targets)
                {
                    await (target == Id ? FenceSourceStream(Id, pair.Key, pair.Value.Generation)
                        : GrainFactory.GetGrain<INeuronGrain>(target.ToGrainId()).FenceSourceStream(Id, pair.Key, pair.Value.Generation))
                        .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                }
                await _fenceMutation.WaitAsync().ConfigureAwait(true);
                try
                {
                    var state = Load();
                    if (state.Subjects.TryGetValue(pair.Key, out var current)
                        && current.Generation == pair.Value.Generation && current.FencePending)
                    {
                        current.FencePending = false;
                        await Commit(state).ConfigureAwait(true);
                    }
                }
                finally
                {
                    _fenceMutation.Release();
                }
            }
            finally
            {
                if (previousPath is null)
                {
                    Orleans.Runtime.RequestContext.Remove(pathKey);
                }
                else
                {
                    Orleans.Runtime.RequestContext.Set(pathKey, previousPath);
                }
            }
        }
    }

    private sealed class FenceReentrancyScope : IDisposable
    {
        // Stable across silos. Only these post-commit fence RPCs carry this ID.
        private static readonly Guid FenceId = new("c04e260c-953d-4f0f-b388-4f30a1c938db");
        private readonly Guid _previous = Orleans.Runtime.RequestContext.ReentrancyId;
        private readonly IDisposable _section;

        public FenceReentrancyScope()
        {
            Orleans.Runtime.RequestContext.ReentrancyId = FenceId;
            try
            {
                _section = Orleans.Runtime.RequestContext.AllowCallChainReentrancy();
            }
            catch
            {
                Orleans.Runtime.RequestContext.ReentrancyId = _previous;
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                _section.Dispose();
            }
            finally
            {
                Orleans.Runtime.RequestContext.ReentrancyId = _previous;
            }
        }
    }
}
