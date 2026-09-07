using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Core;

[GrainType("application")]
internal sealed partial class ApplicationKernel(
    [PersistentState("application", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ApplicationState> storage,
    TimeProvider clock,
    ApplicationWorkerCapabilityAuthority workerCapabilities) : Grain, IApplicationKernel
{
    public async Task<Guid> SubmitDeclared(Guid operationId, string operation, string payload)
    {
        Authorize();
        using var parsed = System.Text.Json.JsonDocument.Parse(payload);
        var revision = storage.State.Work.TryGetValue(operationId, out var existing)
            ? existing.Revision : await Catalog.Head(this.GetPrimaryKeyString().Split('/')[2]);
        if (operation.StartsWith('$')) { throw new InvalidOperationException("Configuration operations cannot be invoked directly."); }
        var declaration = storage.State.Revisions[revision].Operations.SingleOrDefault(item => item.Key == operation)
            ?? throw new InvalidOperationException("The application does not declare this operation.");
        RequireCallable(declaration);
        return await SubmitPinned(operationId, revision, operation, declaration.RequestContract,
            declaration.ResponseContract, payload);
    }

    public Task<ApplicationInvocation> ReadInvocation(Guid operationId)
    {
        Authorize();
        var work = storage.State.Work.TryGetValue(operationId, out var existing) ? existing
            : throw new InvalidOperationException("Unknown application operation.");
        return Task.FromResult(new ApplicationInvocation(work.Id, work.Revision, work.Operation,
            work.Result.Status, work.Result.Value, work.Result.Error));
    }

    public async Task Install(ApplicationManifest manifest, bool activate = true)
    {
        Authorize();
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Revision);
        if (manifest.Operations.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != manifest.Operations.Length) { throw new InvalidOperationException("Application operations require unique names."); }
        foreach (var operation in manifest.Operations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation.RequestContract);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation.ResponseContract);
        }
        if (storage.State.Revisions.TryGetValue(manifest.Revision, out var existing))
        {
            if (!ManifestEquals(existing, manifest))
            {
                throw new InvalidOperationException("An immutable revision cannot change its declared contracts.");
            }
        }
        var scopes = new Dictionary<string, ApplicationSharedState>(storage.State.Scopes);
        foreach (var operation in manifest.Operations.Where(operation => operation.StateScope is not null))
        {
            if (operation.StateSchema is null) { throw new InvalidOperationException("Shared state requires a declared schema."); }
            if (scopes.TryGetValue(operation.StateScope!, out var scope) && scope.Schema != operation.StateSchema)
            {
                throw new InvalidOperationException("An existing state scope cannot change its schema implicitly.");
            }
            scopes.TryAdd(operation.StateScope!, new(operation.StateSchema, 0, []));
        }
        var revisions = new Dictionary<string, ApplicationManifest>(storage.State.Revisions) { [manifest.Revision] = manifest };
        await Commit(storage.State with { Revisions = revisions, Scopes = scopes });
        if (activate) { _ = await Catalog.Activate(this.GetPrimaryKeyString().Split('/')[2], manifest); }
    }

    public Task<Guid[]> Activate(string revision)
    {
        Authorize();
        if (!storage.State.Revisions.TryGetValue(revision, out var manifest))
        {
            throw new InvalidOperationException("The requested application revision is not installed.");
        }
        return Catalog.Activate(this.GetPrimaryKeyString().Split('/')[2], manifest);
    }

    private static bool ManifestEquals(ApplicationManifest left, ApplicationManifest right)
        => left.Operations.OrderBy(x => x.Key, StringComparer.Ordinal)
                .SequenceEqual(right.Operations.OrderBy(x => x.Key, StringComparer.Ordinal))
            && (left.Outputs ?? []).OrderBy(x => x.BehaviorKey, StringComparer.Ordinal)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .SequenceEqual((right.Outputs ?? []).OrderBy(x => x.BehaviorKey, StringComparer.Ordinal)
                    .ThenBy(x => x.Key, StringComparer.Ordinal))
            && (left.Connections ?? []).OrderBy(x => x.Key, StringComparer.Ordinal)
                .SequenceEqual((right.Connections ?? []).OrderBy(x => x.Key, StringComparer.Ordinal));

    public async Task<Guid> Submit(Guid operationId, string operation, string requestContract, string responseContract, string payload)
    {
        Authorize();
        var revision = storage.State.Work.TryGetValue(operationId, out var existing)
            ? existing.Revision
            : await Catalog.Head(this.GetPrimaryKeyString().Split('/')[2]);
        var declaration = storage.State.Revisions[revision].Operations.SingleOrDefault(item => item.Key == operation)
            ?? throw new InvalidOperationException("The application does not declare this operation.");
        RequireCallable(declaration);
        return await SubmitPinned(operationId, revision, operation, requestContract, responseContract, payload);
    }

    private static void RequireCallable(ApplicationOperation declaration)
    {
        if (declaration.Kind != ApplicationOperationKind.Command)
        {
            throw new InvalidOperationException("Application event inputs cannot be invoked directly.");
        }
    }

    public async Task<Guid> SubmitPinned(Guid operationId, string revision, string operation, string requestContract, string responseContract,
        string payload, ApplicationParent? parent = null)
    {
        Authorize();
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation identity is required.", nameof(operationId));
        }
        var state = storage.State;
        if (state.Work.TryGetValue(operationId, out var existing))
        {
            var original = state.Revisions[existing.Revision].Operations.Single(x => x.Key == existing.Operation);
            if (existing.Revision != revision || existing.Operation != operation || existing.Payload != payload || existing.Parent != parent
                || original.RequestContract != requestContract || original.ResponseContract != responseContract)
            {
                throw new InvalidOperationException("An operation identity cannot be reused for different input.");
            }
            return existing.Id;
        }
        if (!state.Revisions.TryGetValue(revision, out var manifest))
        {
            throw new InvalidOperationException("The requested application revision is not installed.");
        }
        var declaration = manifest.Operations.SingleOrDefault(x => x.Key == operation)
            ?? throw new InvalidOperationException("The operation is not declared by the active application.");
        if (declaration.RequestContract != requestContract || declaration.ResponseContract != responseContract) { throw new InvalidOperationException("The operation's wire contracts do not match this caller."); }
        if (state.Work.Values.Count(x => x.Result.Status is "pending" or "waiting") >= 256) { throw new InvalidOperationException("The application's pending input capacity is exhausted."); }
        if (parent is not null)
        {
            var identity = this.GetPrimaryKeyString();
            var scope = identity[..(identity.LastIndexOf('/') + 1)];
            if (!parent.ApplicationIdentity.StartsWith(scope, StringComparison.Ordinal)
                || parent.ApplicationIdentity.Split('/').Length != 3
                || parent.OperationId == Guid.Empty
                || (parent.ApplicationIdentity == identity && parent.OperationId == operationId))
            {
                throw new InvalidOperationException("A child operation requires an existing parent in the same brain principal scope.");
            }
        }
        var cancelled = parent is not null && await ParentCancelled(parent, 64);
        var work = new ApplicationWork(operationId, revision, operation, payload, new(cancelled ? "cancelled" : "pending"))
        {
            Parent = parent,
            StateScope = declaration.StateScope,
            AdmissionIndex = state.NextAdmission,
        };
        await Commit(state with { NextAdmission = checked(state.NextAdmission + 1), Work = new(state.Work) { [work.Id] = work } });
        return work.Id;
    }

    public async Task<ApplicationClaim?> Claim(string revision)
    {
        Authorize();
        AuthorizeWorker(revision);
        if (!storage.State.Revisions.ContainsKey(revision)) { throw new InvalidOperationException("The worker revision is not installed."); }
        var now = clock.GetUtcNow();
        foreach (var original in storage.State.Work.Values)
        {
            var work = original;
            if (work.Revision == revision && work.Result.Status is "pending" or "waiting"
                && work.Parent is { } parent && await ParentCancelled(parent, 64))
            {
                await Save(work with { Result = new("cancelled") });
                continue;
            }
            if (work.Revision != revision || work.Result.Status is not ("pending" or "waiting") ||
                work.LeaseUntil > now) { continue; }
            if (work.Result.Status == "waiting")
            {
                if (DelayIsDue(work, now)) { }
                else if (await ResolveEventWaitAsync(work) is { } resolved) { work = resolved; }
                else { continue; }
            }
            if (HasEarlierStateInput(work)) { continue; }
            if (work.Attempts >= 3)
            {
                await Save(work with { Result = new("failed", Error: "Worker recovery attempts exhausted.") });
                continue;
            }
            var claimed = WithInitialState(work) with
            {
                Result = new("pending"), Lease = Guid.NewGuid(), LeaseUntil = now.AddSeconds(30),
                Attempts = work.Attempts + 1,
            };
            await Save(claimed);
            var operation = storage.State.Revisions[revision].Operations.Single(operation => operation.Key == claimed.Operation);
            return new(claimed.Id, claimed.Lease, revision, claimed.Operation, claimed.Payload,
                claimed.StateScope, operation.StateSchema, claimed.InitialState);
        }
        return null;
    }

    public Task Complete(Guid operationId, Guid lease, string result, int effectCount, int branchEffectCount,
        Dictionary<string, string> writes)
    {
        if (IsCancelled(operationId, lease)) { return Task.CompletedTask; }
        var work = Owned(operationId, lease);
        if (work.Effects.Count != effectCount)
        {
            throw new InvalidOperationException("Determinism violation: execution skipped a recorded effect.");
        }
        if (work.BranchEffects.Count != branchEffectCount)
        {
            throw new InvalidOperationException("Determinism violation: execution skipped a recorded branch effect.");
        }
        return SaveCheckpoint(work with { Result = new("completed", result) }, writes);
    }

    public Task Fail(Guid operationId, Guid lease, string error)
        => IsCancelled(operationId, lease)
            ? Task.CompletedTask
            : Save(Owned(operationId, lease) with { Result = new("failed", Error: error) });

    public async Task<bool> Renew(Guid operationId, Guid lease)
    {
        if (IsCancelled(operationId, lease)) { return false; }
        var work = Owned(operationId, lease);
        if (work.Parent is { } parent && await ParentCancelled(parent, 64))
        {
            await Save(work with { Result = new("cancelled") });
            return false;
        }
        await Save(work with { LeaseUntil = clock.GetUtcNow().AddSeconds(30) });
        return true;
    }

    public async Task<bool> CancellationRequested(Guid operationId, int remainingDepth)
    {
        Authorize();
        if (remainingDepth <= 0) { throw new InvalidOperationException("Application invocation nesting exceeds 64 parents."); }
        var work = storage.State.Work.GetValueOrDefault(operationId)
            ?? throw new InvalidOperationException("The parent application operation is unknown.");
        if (work.Result.Status == "cancelled") { return true; }
        if (work.Result.Status is "completed" or "failed") { return false; }
        return work.Parent is { } parent && await ParentCancelled(parent, remainingDepth - 1);
    }

    private Task<bool> ParentCancelled(ApplicationParent parent, int remainingDepth)
        => parent.ApplicationIdentity == this.GetPrimaryKeyString()
            ? CancellationRequested(parent.OperationId, remainingDepth)
            : GrainFactory.GetGrain<IApplicationKernel>(parent.ApplicationIdentity)
                .CancellationRequested(parent.OperationId, remainingDepth);

    public Task Cancel(Guid operationId)
    {
        Authorize();
        var work = storage.State.Work.TryGetValue(operationId, out var existing)
            ? existing
            : throw new InvalidOperationException("Unknown application operation.");
        if (work.Result.Status is "completed" or "failed" or "cancelled")
        {
            return Task.CompletedTask;
        }
        return Save(work with { Result = new("cancelled") });
    }

    public Task<ApplicationResult> Read(Guid operationId)
    {
        Authorize();
        return Task.FromResult(storage.State.Work.TryGetValue(operationId, out var work)
            ? work.Result : throw new InvalidOperationException("Unknown application operation."));
    }

    public Task<ApplicationResult?> ReadIfKnown(Guid operationId)
    {
        Authorize();
        return Task.FromResult(storage.State.Work.TryGetValue(operationId, out var work) ? work.Result : null);
    }

    public Task<string> Head()
    {
        Authorize();
        return Catalog.Head(this.GetPrimaryKeyString().Split('/')[2]);
    }

    public async Task<string[]> PendingRevisions()
    {
        Authorize();
        var applicationKey = this.GetPrimaryKeyString().Split('/')[2];
        var deliveries = await Catalog.PendingDeliveryRevisions(applicationKey);
        return storage.State.Work.Values.Where(work => work.Result.Status is "pending" or "waiting")
            .Select(work => work.Revision).Concat(deliveries).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
    }

    public Task<bool> Declares(string revision, string operation)
    {
        Authorize();
        return Task.FromResult(storage.State.Revisions.TryGetValue(revision, out var manifest)
            && manifest.Operations.Any(candidate => candidate.Key == operation));
    }

    public async Task<ApplicationEffect> BeginEffect(Guid operationId, Guid lease, int ordinal, string identity, string? revision, Dictionary<string, string> writes)
    {
        var work = Owned(operationId, lease);
        if (IsQuery(work)) { throw new InvalidOperationException("Queries cannot emit effects."); }
        if (ordinal < 0 || ordinal > work.Effects.Count)
        {
            throw new InvalidOperationException("The effect sequence has a gap.");
        }
        if (ordinal < work.Effects.Count)
        {
            var recorded = work.Effects[ordinal];
            if (recorded.Identity != identity || !SameWrites(recorded.Writes, writes))
            {
                throw new InvalidOperationException("Determinism violation: the recorded effect sequence changed.");
            }
            return recorded;
        }
        var effect = new ApplicationEffect(identity, Guid.NewGuid(), revision) { Writes = new(writes) };
        await SaveCheckpoint(work with { Effects = [.. work.Effects, effect] }, writes);
        return effect;
    }

    public async Task<bool> BeginDelay(Guid operationId, Guid lease, int ordinal, string identity,
        long durationTicks, Dictionary<string, string> writes)
    {
        if (durationTicks < 0) { throw new ArgumentOutOfRangeException(nameof(durationTicks)); }
        var work = Owned(operationId, lease);
        if (IsQuery(work)) { throw new InvalidOperationException("Queries cannot wait on durable delays."); }
        if (ordinal < 0 || ordinal > work.Effects.Count) { throw new InvalidOperationException("The effect sequence has a gap."); }
        var now = clock.GetUtcNow();
        if (ordinal < work.Effects.Count)
        {
            var recorded = work.Effects[ordinal];
            if (recorded.Identity != identity || !SameWrites(recorded.Writes, writes) || recorded.DueAt is null)
            {
                throw new InvalidOperationException("Determinism violation: the recorded delay changed.");
            }
            if (recorded.DueAt > now)
            {
                await Save(work with
                {
                    Result = new("waiting"), Lease = Guid.Empty, LeaseUntil = DateTimeOffset.MinValue,
                    Attempts = 0,
                });
                return false;
            }
            return true;
        }
        var effect = new ApplicationEffect(identity, Guid.NewGuid(), Revision: null)
        {
            Writes = new(writes),
            DueAt = now.Add(TimeSpan.FromTicks(durationTicks)),
        };
        if (durationTicks == 0)
        {
            await SaveCheckpoint(work with { Effects = [.. work.Effects, effect] }, writes);
            return true;
        }
        await SaveCheckpoint(work with
        {
            Effects = [.. work.Effects, effect], Result = new("waiting"),
            Lease = Guid.Empty, LeaseUntil = DateTimeOffset.MinValue, Attempts = 0,
        }, writes);
        return false;
    }

    public async Task ArmEventWait(Guid operationId, Guid lease, int ordinal, string identity,
        string sourceKind, string sourceId, string behaviorKey, string outputKey, string contract,
        Dictionary<string, string> writes)
    {
        var work = Owned(operationId, lease);
        if (IsQuery(work)) { throw new InvalidOperationException("Queries cannot wait for events."); }
        if (ordinal < 0 || ordinal > work.Effects.Count) { throw new InvalidOperationException("The effect sequence has a gap."); }
        if (ordinal < work.Effects.Count)
        {
            var recorded = work.Effects[ordinal];
            if (recorded.Identity != identity || recorded.WaitSourceKind != sourceKind ||
                recorded.WaitSourceId != sourceId || recorded.WaitBehaviorKey != behaviorKey ||
                recorded.WaitOutputKey != outputKey || recorded.WaitContract != contract ||
                !SameWrites(recorded.Writes, writes))
            {
                throw new InvalidOperationException("Determinism violation: the recorded event wait changed.");
            }
            return;
        }
        var cursor = sourceKind switch
        {
            "neuron" => (await GrainFactory.GetGrain<INeuronQuery>(ParseWaitSource(sourceId).ToGrainId())
                .ReadJournal(JournalKind.Outgoing, long.MaxValue)).ResumeSequence,
            "application" => await Catalog.EventCursor(
                new(sourceKind, sourceId, behaviorKey, outputKey, contract)),
            _ => throw new InvalidOperationException("An event wait source kind is unsupported."),
        };
        var effect = new ApplicationEffect(identity, Guid.NewGuid(), Revision: null)
        {
            Writes = new(writes), WaitSourceKind = sourceKind, WaitSourceId = sourceId,
            WaitBehaviorKey = behaviorKey, WaitOutputKey = outputKey, WaitContract = contract,
            WaitAfterSequence = cursor,
        };
        await SaveCheckpoint(work with { Effects = [.. work.Effects, effect] }, writes);
    }

    public async Task<string?> AwaitEventWait(Guid operationId, Guid lease, int ordinal, string identity)
    {
        var work = Owned(operationId, lease);
        if (ordinal < 0 || ordinal >= work.Effects.Count)
        {
            throw new InvalidOperationException("The event wait was not armed.");
        }
        var effect = work.Effects[ordinal];
        if (effect.Identity != identity || effect.WaitSourceId is null)
        {
            throw new InvalidOperationException("Determinism violation: the armed event wait changed.");
        }
        if (effect.Result is not null) { return effect.Result; }
        if (await ResolveEventWaitAsync(work, ordinal) is { } resolved)
        {
            await Save(resolved);
            return resolved.Effects[ordinal].Result;
        }
        await Save(work with
        {
            Result = new("waiting"), Lease = Guid.Empty, LeaseUntil = DateTimeOffset.MinValue,
            Attempts = 0, WaitingEffectOrdinal = ordinal,
        });
        return null;
    }

    public Task FinishEffect(Guid operationId, Guid lease, int ordinal, string result)
    {
        var work = Owned(operationId, lease);
        if (ordinal < 0 || ordinal >= work.Effects.Count)
        {
            throw new InvalidOperationException("The effect was not admitted.");
        }
        var effect = work.Effects[ordinal];
        if (effect.Result is not null && effect.Result != result)
        {
            throw new InvalidOperationException("A recorded effect result cannot change.");
        }
        var effects = new List<ApplicationEffect>(work.Effects) { [ordinal] = effect with { Result = result } };
        return Save(work with { Effects = effects });
    }

    public async Task<ApplicationEffect> BeginBranchEffect(
        Guid operationId, Guid lease, string branch, string identity, string? revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        var work = Owned(operationId, lease);
        if (IsQuery(work)) { throw new InvalidOperationException("Queries cannot emit effects."); }
        if (work.BranchEffects.TryGetValue(branch, out var recorded))
        {
            if (recorded.Identity != identity)
            {
                throw new InvalidOperationException("Determinism violation: a recorded branch effect changed.");
            }
            return recorded;
        }
        var effect = new ApplicationEffect(identity, Guid.NewGuid(), revision);
        await Save(work with
        {
            BranchEffects = new(work.BranchEffects, StringComparer.Ordinal) { [branch] = effect },
        });
        return effect;
    }

    public Task FinishBranchEffect(Guid operationId, Guid lease, string branch, string result)
    {
        var work = Owned(operationId, lease);
        if (!work.BranchEffects.TryGetValue(branch, out var effect))
        {
            throw new InvalidOperationException("The branch effect was not admitted.");
        }
        if (effect.Result is not null && effect.Result != result)
        {
            throw new InvalidOperationException("A recorded branch effect result cannot change.");
        }
        return Save(work with
        {
            BranchEffects = new(work.BranchEffects, StringComparer.Ordinal)
            {
                [branch] = effect with { Result = result },
            },
        });
    }

    private ApplicationWork Owned(Guid id, Guid lease)
    {
        Authorize();
        if (!storage.State.Work.TryGetValue(id, out var work) || work.Result.Status != "pending"
            || work.Lease != lease || work.LeaseUntil <= clock.GetUtcNow())
        {
            throw new InvalidOperationException("The execution lease is stale or the operation has completed.");
        }
        AuthorizeWorker(work.Revision);
        return work;
    }

    private bool IsCancelled(Guid id, Guid lease)
    {
        Authorize();
        if (!storage.State.Work.TryGetValue(id, out var work) ||
            work.Result.Status != "cancelled" ||
            work.Lease != lease)
        {
            return false;
        }
        AuthorizeWorker(work.Revision);
        return true;
    }

    private void AuthorizeWorker(string revision)
    {
        var actor = VerifiedActor.Current ?? throw new InvalidOperationException("An authenticated application principal is required.");
        var key = this.GetPrimaryKeyString().Split('/');
        if (key.Length != 3) { throw new InvalidOperationException("The application identity is invalid."); }
        workerCapabilities.Validate(ApplicationWorkerCapabilityContext.Current, key[0], actor.PrincipalId.Value,
            key[2], revision, clock.GetUtcNow());
    }

    private static bool DelayIsDue(ApplicationWork work, DateTimeOffset now)
        => work.Effects.LastOrDefault()?.DueAt is { } dueAt && dueAt <= now;

    private Task<ApplicationWork?> ResolveEventWaitAsync(ApplicationWork work)
        => work.WaitingEffectOrdinal is { } ordinal
            ? ResolveEventWaitAsync(work, ordinal)
            : Task.FromResult<ApplicationWork?>(null);

    private async Task<ApplicationWork?> ResolveEventWaitAsync(ApplicationWork work, int ordinal)
    {
        if (ordinal < 0 || ordinal >= work.Effects.Count) { return null; }
        var effect = work.Effects[ordinal];
        if (effect.WaitSourceId is null || effect.Result is not null) { return null; }
        if (effect.WaitSourceKind == "application")
        {
            var payload = await Catalog.ReadEventAfter(new("application", effect.WaitSourceId!,
                effect.WaitBehaviorKey!, effect.WaitOutputKey!, effect.WaitContract!), effect.WaitAfterSequence!.Value);
            if (payload is null) { return null; }
            var applicationEffects = new List<ApplicationEffect>(work.Effects)
            {
                [ordinal] = effect with { Result = payload },
            };
            return work with { Effects = applicationEffects, WaitingEffectOrdinal = null };
        }
        if (effect.WaitSourceKind != "neuron") { return null; }
        var source = ParseWaitSource(effect.WaitSourceId!);
        var query = GrainFactory.GetGrain<INeuronQuery>(source.ToGrainId());
        var read = await query.ReadJournal(JournalKind.Outgoing, effect.WaitAfterSequence!.Value);
        var retained = await SignalRequestPolicy.RecoverRetainedAsync(read,
            sequence => query.ReadJournal(JournalKind.Outgoing, sequence));
        var delivery = retained.Delta.FirstOrDefault(candidate => candidate.Principal == VerifiedActor.Current!.PrincipalId &&
            HasContract(candidate.Signal, effect.WaitContract!));
        if (delivery is null) { return null; }
        var effects = new List<ApplicationEffect>(work.Effects)
        {
            [ordinal] = effect with { Result = System.Text.Json.JsonSerializer.Serialize(delivery.Signal, delivery.Signal.GetType()) },
        };
        return work with { Effects = effects, WaitingEffectOrdinal = null };
    }

    private NeuronId ParseWaitSource(string sourceId)
    {
        var separator = sourceId.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == sourceId.Length - 1)
        {
            throw new InvalidOperationException("An event wait source is invalid.");
        }
        var source = NeuronId.FromGrainKey(sourceId[..separator], sourceId[(separator + 1)..]);
        var owner = this.GetPrimaryKeyString().Split('/')[0];
        if (source.Owner.Value != owner) { throw new InvalidOperationException("An event wait source belongs to another owner."); }
        return source;
    }

    private static bool HasContract(Signal signal, string contract)
    {
        if (Attribute.GetCustomAttribute(signal.GetType(), typeof(ApplicationJsonContractAttribute))
            is ApplicationJsonContractAttribute declared)
        {
            return contract == $"{declared.StableName}/v{declared.SchemaVersion}";
        }
        return signal is DigitalBrainActivated && contract == "brain.activated/v1";
    }

    private Task Save(ApplicationWork work)
        => Commit(storage.State with { Work = new(storage.State.Work) { [work.Id] = work } });

    private async Task Commit(ApplicationState next)
    {
        var previous = storage.State;
        storage.State = next;
        try { await storage.WriteStateAsync(); }
        catch { storage.State = previous; throw; }
    }

    private void Authorize()
    {
        var actor = VerifiedActor.Current ?? throw new InvalidOperationException("An authenticated application principal is required.");
        var key = this.GetPrimaryKeyString().Split('/');
        if (key.Length != 3 || key[1] != actor.PrincipalId.ToString()) { throw new InvalidOperationException("The application belongs to another principal."); }
    }

    private IApplicationCatalog Catalog => GrainFactory.GetGrain<IApplicationCatalog>(
        string.Join('/', this.GetPrimaryKeyString().Split('/')[..2]));
}

