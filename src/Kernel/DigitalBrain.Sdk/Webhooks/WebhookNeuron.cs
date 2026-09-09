using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Sdk.Webhooks;

public sealed record WebhookFact(string EventId, Signal Signal, SignalDelivery? ReplyTo = null);
public sealed record WebhookApplication(IReadOnlyList<WebhookFact> Facts, Action? Rollback = null);
/// <summary>
/// Durable intake and independent recipient progress. All provider and subscriber I/O runs in
/// WebhookWorker; this neuron owns short state transitions and source-owned native subscriptions.
/// </summary>
public abstract class WebhookNeuron : Neuron, IWebhook, IAuthenticatedWebhookIngress, IRemindable, INeuronGrain
{
    private const string ReminderName = "sdk.webhook.recovery";
    private const int Capacity = 4096;
    private readonly IDurableValue<byte[]> _value;
    private readonly Serializer<WebhookState> _serializer;
    private readonly WebhookWakeups _wakeups;
    private WebhookState? _loaded;
    protected WebhookNeuron(NeuronRuntime runtime) : base(runtime)
    {
        _value = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>("sdk.webhook");
        _serializer = ServiceProvider.GetRequiredService<Serializer<WebhookState>>();
        _wakeups = ServiceProvider.GetRequiredService<WebhookWakeups>();
    }

    protected abstract ActorContext SourceActor { get; }
    protected abstract string SourceEpoch { get; }
    protected virtual bool SourceAvailable => true;
    protected virtual string? SourceDetail => null;

    protected virtual void AuthorizeReceipt(WebhookReceipt receipt) => RequireActor();
    protected virtual bool CanAccept(WebhookReceipt receipt) => SourceAvailable;
    protected virtual bool CanDeliver(Signal signal) => SourceAvailable;
    protected virtual Action? StageAcceptance(WebhookReceipt receipt) => null;
    protected virtual void ReceiptAccepted(WebhookReceipt receipt)
    {
    }

    protected virtual Task OnSourceRecoveryAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnSubscriptionsChangedAsync() => Task.CompletedTask;
    // Opt in only for equivalent state refreshes. Arbitrary provider events retain independent processing.
    protected virtual bool CanCoalesce(WebhookReceipt first, WebhookReceipt next) => false;
    protected virtual WebhookApplication ApplyReceipt(WebhookReceipt receipt, Signal[] results) => new(results.Select((signal, index) => new WebhookFact($"{receipt.Epoch}:{receipt.DeliveryId}:{index}", signal)).ToArray());
    protected void RequireActor()
    {
        if (VerifiedActor.Current?.PrincipalId != SourceActor.PrincipalId)
        {
            throw new NeuronAuthorizationException("The webhook belongs to a different authenticated principal.");
        }
    }

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        await base.OnNeuronActivatedAsync(cancellationToken);
        using var actor = VerifiedActor.Enter(SourceActor);
        await OnSourceRecoveryAsync(cancellationToken);
        await RefreshSourceEpochAsync();
        if (HasPending || (await ReadSynapses()).Count > 0)
        {
            await EnsureRecoveryAsync();
        }

        Wake();
    }

    async Task INeuronGrain.BindOutgoing(NeuronId subscriber, string signalType, CorrelationId? correlation)
    {
        RequireSubscriber(subscriber);
        await RefreshSourceEpochAsync();
        if (!SourceAvailable)
        {
            throw new NeuronAuthorizationException("The webhook source is not ready for subscriptions.");
        }

        await EnsureRecoveryAsync();
        await base.BindOutgoing(subscriber, signalType, correlation);
        var fences = State.Fences.Where(fence => fence.Recipient != subscriber).ToList();
        fences.Add(new(subscriber, State.Generation));
        await SaveAsync(State with { Fences = fences });
        await OnSubscriptionsChangedAsync();
        Wake();
    }

    async Task INeuronGrain.UnbindOutgoing(NeuronId subscriber, string signalType, CorrelationId? correlation)
    {
        RequireSubscriber(subscriber);
        await base.UnbindOutgoing(subscriber, signalType, correlation);
        await OnSubscriptionsChangedAsync();
        Wake();
    }

    private void RequireSubscriber(NeuronId subscriber)
    {
        RequireActor();
        if (subscriber.Owner != Id.Owner || !PrincipalPartition.OwnsInstance(SourceActor.PrincipalId, subscriber.Name))
        {
            throw new NeuronAuthorizationException("Webhook subscriptions must remain within their principal and owner.");
        }
    }

    public Task HandleAsync(ReadWebhook signal, CancellationToken cancellationToken)
    {
        RequireActor();
        return ReplyAsync(new WebhookStatus(SourceAvailable, State.Receipts.Count(item => !item.Completed), State.Deliveries.Sum(item => item.Recipients.Length) + State.Fences.Count, State.LastAcceptedAt, SourceDetail));
    }

    public async Task<WebhookAcceptance> AcceptAsync(WebhookReceipt receipt)
    {
        AuthorizeReceipt(receipt);
        receipt = WebhookTrace.Sanitize(receipt);
        await RefreshSourceEpochAsync();
        if (receipt.Epoch != SourceEpoch || string.IsNullOrWhiteSpace(receipt.DeliveryId) || receipt.DeliveryId.Length > 200 || receipt.Digest.Length != 64)
        {
            return WebhookAcceptance.Conflict;
        }

        // A duplicate must repair the wake/reminder seam too.
        await EnsureRecoveryAsync();
        var existing = State.Receipts.FirstOrDefault(item => item.Receipt.DeliveryId == receipt.DeliveryId);
        if (existing is not null)
        {
            Wake();
            return existing.Receipt.Digest == receipt.Digest && existing.Receipt.Epoch == receipt.Epoch ? WebhookAcceptance.Duplicate : WebhookAcceptance.Conflict;
        }

        if (!CanAccept(receipt))
        {
            return WebhookAcceptance.Unavailable;
        }

        var cutoff = TimeProvider.GetUtcNow().AddDays(-7);
        var retained = State.Receipts.Where(item => !item.Completed || item.Receipt.AcceptedAt >= cutoff).ToList();
        if (retained.Count >= Capacity)
        {
            return WebhookAcceptance.Unavailable;
        }

        retained.Add(new(receipt));
        var rollback = StageAcceptance(receipt);
        try
        {
            await SaveAsync(await AlignEpochAsync(State with { Receipts = retained, LastAcceptedAt = receipt.AcceptedAt }));
        }
        catch
        {
            rollback?.Invoke();
            throw;
        }

        ReceiptAccepted(receipt);
        Wake();
        return WebhookAcceptance.Accepted;
    }

    public async Task<WebhookWork?> ClaimAsync()
    {
        RequireActor();
        await RefreshSourceEpochAsync();
        var now = TimeProvider.GetUtcNow();
        var state = State;
        var fenceIndex = state.Fences.FindIndex(fence => fence.DueAt is null || fence.DueAt <= now);
        if (fenceIndex >= 0)
        {
            var fence = state.Fences[fenceIndex];
            var lease = Guid.NewGuid();
            var fences = state.Fences.ToList();
            fences[fenceIndex] = fence with
            {
                Lease = lease,
                DueAt = now.AddMinutes(2)
            };
            await SaveAsync(state with { Fences = fences });
            return new(lease, null, null, fence.Recipient, FenceEpoch: fence.Generation);
        }

        // Remove only obsolete recipient routes; successful and failing recipients progress independently.
        var deliveries = new List<PendingWebhookDelivery>();
        foreach (var item in state.Deliveries)
        {
            if (item.Epoch != SourceEpoch || item.Generation != state.Generation || !CanDeliver(item.Signal))
            {
                continue;
            }

            var routes = item.ReplyTo is { } reply ? new HashSet<NeuronId>
            {
                reply.Caller
            }

            : BroadcastRecipients(item.Signal).ToHashSet();
            var recipients = item.Recipients.Where(recipient => routes.Contains(recipient.Target)).ToArray();
            if (recipients.Length > 0)
            {
                deliveries.Add(item with { Recipients = recipients });
            }
        }

        if (deliveries.Count != state.Deliveries.Count || deliveries.Where((item, index) => item.Recipients.Length != state.Deliveries[index].Recipients.Length).Any())
        {
            await SaveAsync(state with { Deliveries = deliveries });
            state = State;
        }

        for (var index = 0; index < state.Deliveries.Count; index++)
        {
            var item = state.Deliveries[index];
            var recipientIndex = Array.FindIndex(item.Recipients, recipient => (recipient.DueAt is null || recipient.DueAt <= now) && !state.Fences.Any(fence => fence.Recipient == recipient.Target));
            if (recipientIndex < 0)
            {
                continue;
            }

            var lease = Guid.NewGuid();
            var recipients = item.Recipients.ToArray();
            recipients[recipientIndex] = recipients[recipientIndex] with
            {
                Lease = lease,
                DueAt = now.AddMinutes(2)
            };
            var envelope = item.Delivery ?? CreateDelivery(item.Signal, item.ReplyTo, sourceEpoch: item.Generation);
            var next = state with
            {
                Deliveries = [.. state.Deliveries]
            };
            next.Deliveries[index] = item with
            {
                Recipients = recipients,
                Delivery = envelope
            };
            var previous = State;
            Stage(next);
            try
            {
                if (item.Delivery is null)
                {
                    await RecordOutgoingAsync(envelope);
                }
                else
                {
                    await WriteStateAsync();
                }
            }
            catch
            {
                Stage(previous);
                throw;
            }

            return new(lease, null, envelope, recipients[recipientIndex].Target, item.ReplyTo is not null);
        }

        var pending = state.Receipts.FindIndex(item => !item.Completed && (item.DueAt is null || item.DueAt <= now));
        if (pending < 0)
        {
            return null;
        }

        var receiptLease = Guid.NewGuid();
        var receipts = state.Receipts.ToList();
        receipts[pending] = receipts[pending] with
        {
            Lease = receiptLease,
            DueAt = now.AddMinutes(2)
        };
        for (var index = pending + 1; index < receipts.Count; index++)
        {
            var candidate = receipts[index];
            if (candidate.Completed)
            {
                continue;
            }

            if (candidate.DueAt > now || candidate.Receipt.Epoch != receipts[pending].Receipt.Epoch || !CanCoalesce(receipts[pending].Receipt, candidate.Receipt))
            {
                break;
            }

            receipts[index] = candidate with
            {
                Lease = receiptLease,
                DueAt = now.AddMinutes(2)
            };
        }

        await SaveAsync(state with { Receipts = receipts });
        return new(receiptLease, receipts[pending].Receipt, null, null);
    }

    public async Task CompleteAsync(Guid lease, Signal[] results)
    {
        RequireActor();
        await RefreshSourceEpochAsync();
        var index = State.Receipts.FindIndex(item => !item.Completed && item.Lease == lease);
        if (index < 0)
        {
            return;
        }

        var pending = State.Receipts[index];
        var receipts = State.Receipts.ToList();
        for (var receiptIndex = 0; receiptIndex < receipts.Count; receiptIndex++)
        {
            if (!receipts[receiptIndex].Completed && receipts[receiptIndex].Lease == lease)
            {
                receipts[receiptIndex] = receipts[receiptIndex] with
                {
                    Completed = true,
                    Lease = default,
                    DueAt = null
                };
            }
        }

        if (pending.Receipt.Epoch != SourceEpoch)
        {
            await SaveAsync(State with { Receipts = receipts });
            return;
        }

        var applied = ApplyReceipt(pending.Receipt, results);
        try
        {
            var deliveries = State.Deliveries.ToList();
            foreach (var fact in applied.Facts)
            {
                AddFact(deliveries, fact.EventId, fact.Signal, fact.ReplyTo);
                if (fact.ReplyTo is null && fact.Signal is not WebhookReceived)
                {
                    AddFact(deliveries, fact.EventId + ":webhook", new WebhookReceived(fact.EventId, fact.Signal, pending.Receipt.AcceptedAt));
                }
            }

            if (deliveries.Count > Capacity)
            {
                throw new InvalidOperationException("The webhook notification capacity is full.");
            }

            await SaveAsync(State with { Receipts = receipts, Deliveries = deliveries });
        }
        catch
        {
            applied.Rollback?.Invoke();
            throw;
        }

        Wake();
    }

    private void AddFact(List<PendingWebhookDelivery> deliveries, string eventId, Signal signal, SignalDelivery? replyTo = null)
    {
        if (deliveries.Any(item => item.EventId == eventId))
        {
            return;
        }

        WebhookRecipient[] recipients = replyTo is null ? BroadcastRecipients(signal).Select(target => new WebhookRecipient(target)).ToArray() : [new WebhookRecipient(replyTo.Caller)];
        if (recipients.Length > 0)
        {
            deliveries.Add(new(eventId, signal, recipients, ReplyTo: replyTo, Epoch: SourceEpoch, Generation: State.Generation));
        }
    }

    public async Task AcknowledgeAsync(Guid lease, bool handled)
    {
        RequireActor();
        if (await FinishFenceAsync(lease, handled))
        {
            return;
        }

        await FinishDeliveryAsync(lease, handled);
    }

    public async Task FailAsync(Guid lease)
    {
        RequireActor();
        if (await FinishFenceAsync(lease, false))
        {
            return;
        }

        var index = State.Receipts.FindIndex(item => !item.Completed && item.Lease == lease);
        if (index < 0)
        {
            await FinishDeliveryAsync(lease, false);
            return;
        }

        var receipts = State.Receipts.ToList();
        for (var receiptIndex = 0; receiptIndex < receipts.Count; receiptIndex++)
        {
            var item = receipts[receiptIndex];
            if (!item.Completed && item.Lease == lease)
            {
                var attempts = Math.Min(16, item.Attempts + 1);
                receipts[receiptIndex] = item with
                {
                    Lease = default,
                    Attempts = attempts,
                    DueAt = RetryAt(attempts)
                };
            }
        }

        await SaveAsync(State with { Receipts = receipts });
    }

    private async Task FinishDeliveryAsync(Guid lease, bool handled)
    {
        RequireActor();
        var deliveries = State.Deliveries.ToList();
        for (var index = 0; index < deliveries.Count; index++)
        {
            var item = deliveries[index];
            var recipient = Array.FindIndex(item.Recipients, target => target.Lease == lease);
            if (recipient < 0)
            {
                continue;
            }

            var recipients = item.Recipients.ToList();
            if (handled)
            {
                recipients.RemoveAt(recipient);
            }
            else
            {
                var attempts = Math.Min(16, recipients[recipient].Attempts + 1);
                recipients[recipient] = recipients[recipient] with
                {
                    Lease = default,
                    Attempts = attempts,
                    DueAt = RetryAt(attempts)
                };
            }

            if (recipients.Count == 0)
            {
                deliveries.RemoveAt(index);
            }
            else
            {
                deliveries[index] = item with
                {
                    Recipients = [.. recipients]
                };
            }

            await SaveAsync(State with { Deliveries = deliveries });
            return;
        }
    }

    protected bool HasPending => State.Receipts.Any(item => !item.Completed) || State.Deliveries.Count > 0 || State.Fences.Count > 0;

    protected async Task RefreshSourceEpochAsync()
    {
        var next = await AlignEpochAsync(State);
        if (!ReferenceEquals(next, State))
        {
            await SaveAsync(next);
            Wake();
        }
    }

    private async Task<WebhookState> AlignEpochAsync(WebhookState state)
    {
        if (state.Epoch == SourceEpoch && state.Available == SourceAvailable)
        {
            return state;
        }

        var generation = checked(state.Generation + 1);
        var recipients = state.Deliveries.SelectMany(delivery => delivery.Recipients.Select(recipient => recipient.Target)).Concat(state.Fences.Select(fence => fence.Recipient)).Concat((await ReadSynapses()).Where(edge => edge.Kind == DigitalBrain.Abstractions.Synapses.SynapseKind.Bound).Select(edge => edge.Target)).Distinct().ToArray();
        return state with
        {
            Epoch = SourceEpoch,
            Available = SourceAvailable,
            Generation = generation,
            Fences = [.. recipients.Select(recipient => new PendingWebhookFence(recipient, generation))]
        };
    }

    private async Task<bool> FinishFenceAsync(Guid lease, bool handled)
    {
        var index = State.Fences.FindIndex(fence => fence.Lease == lease);
        if (index < 0)
        {
            return false;
        }

        var fences = State.Fences.ToList();
        if (handled)
        {
            fences.RemoveAt(index);
        }
        else
        {
            var fence = fences[index];
            var attempts = Math.Min(16, fence.Attempts + 1);
            fences[index] = fence with
            {
                Lease = default,
                DueAt = RetryAt(attempts),
                Attempts = attempts
            };
        }

        await SaveAsync(State with { Fences = fences });
        Wake();
        return true;
    }

    protected bool HasPendingInput<T>()
        where T : Signal => State.Receipts.Any(item => !item.Completed && item.Receipt.Input is T);
    protected void Wake() => _wakeups.Wake(Id, SourceActor);
    private DateTimeOffset RetryAt(int attempts) => TimeProvider.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, attempts)));
    private Task EnsureRecoveryAsync() => this.RegisterOrUpdateReminder(ReminderName, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ReminderName)
        {
            return;
        }

        using var actor = VerifiedActor.Enter(SourceActor);
        await OnSourceRecoveryAsync(CancellationToken.None);
        Wake();
        if (!HasPending && (await ReadSynapses()).Count == 0 && await this.GetReminder(ReminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    private WebhookState State => _loaded ??= _value.Value is { Length: > 0 } bytes
        ? _serializer.Deserialize(bytes) ?? new()
        : new();

    private void Stage(WebhookState next)
    {
        _loaded = next;
        _value.Value = _serializer.SerializeToArray(next);
    }

    private async Task SaveAsync(WebhookState next)
    {
        var previous = State;
        Stage(next);
        try
        {
            await WriteStateAsync();
        }
        catch
        {
            Stage(previous);
            throw;
        }
    }
}
