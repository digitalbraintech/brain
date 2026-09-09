using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed class BrainGraphProjection(IBrainGraphSource source, BrainGraphMetadata? presentationMetadata = null)
{
    private readonly BrainGraphMetadata _metadata = presentationMetadata ?? new([]);
    internal const int MaxActivity = 64;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    internal const string SnapshotScope = "One brain, authored applications, involved neurons, and their source-owned subscriptions. Select an activity to follow its causal execution.";

    public async Task<BrainGraphSnapshot> ReadAsync(
        string chatName, ActorContext actor, CancellationToken cancellationToken)
    {
        using var verifiedActor = VerifiedActor.Enter(actor);
        _ = chatName;
        var inbox = NeuronId.For<IComposer>(source.Owner, IComposer.DefaultInstanceName);
        var session = IBrainNeuron.ForOwner(source.Owner);
        var assistant = new NeuronId("assistant", source.Owner, "assistant");
        var unavailable = new HashSet<NeuronId>();
        NeuronId? activeExecution = null;
        var participants = new HashSet<NeuronId>();
        var known = new HashSet<NeuronId>();
        var pending = new Queue<NeuronId>();
        Discover(inbox);
        Discover(session);
        Discover(NeuronId.For<IActivitySource>(source.Owner, IActivitySource.DefaultInstanceName));
        Discover(NeuronId.For<IActivities>(source.Owner, IActivities.DefaultInstanceName));
        Discover(NeuronId.For<IUIRenderer>(source.Owner, ISurface.DefaultInstanceName));
        var reads = new Dictionary<NeuronId, BrainGraphNeuronRead>();
        var truncated = false;
        while (pending.TryDequeue(out var neuron))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrainGraphNeuronRead read;
            try
            {
                read = await source.ReadAsync(neuron, cancellationToken)
                    .WaitAsync(ReadTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A persisted participant may belong to a module unavailable in this
                // host. Keep its identity, but never invent its unread outgoing edges
                // or expose activation/transport exception details to the browser.
                unavailable.Add(neuron);
                read = new([], new(0, [], null), new(0, [], null));
            }
            reads.Add(neuron, read);
            var privateNeuron = IsPrivate(neuron, actor.PrincipalId, activeExecution);
            var observeShared = ObservesSharedComposition(neuron);
            foreach (var edge in read.Synapses)
            {
                // A shared participant (assistant, owner root) can be used by multiple
                // principal partitions. Never walk its unrelated outgoing graph —
                // except the composer peek, whose Bound targets are the owner's wiring.
                if (edge.Source == neuron
                    && (privateNeuron || observeShared
                        || IsPrivate(edge.Target, actor.PrincipalId, activeExecution)))
                {
                    Discover(edge.Target);
                }
            }

            foreach (var delivery in read.Incoming.Delta.Concat(read.Outgoing.Delta))
            {
                if (!VisibleDelivery(delivery, privateNeuron, actor.PrincipalId, observeShared))
                {
                    continue;
                }

                Discover(delivery.Caller);
                // A removed subscription must remain restorable while its actual
                // subscription event is retained; no separate edge tombstone store.
                if (privateNeuron || observeShared)
                {
                    if (delivery.Signal is Subscribe subscribed)
                    {
                        Discover(subscribed.Source);
                    }
                    if (delivery.Signal is Unsubscribe unsubscribed)
                    {
                        Discover(unsubscribed.Source);
                    }
                }
            }

            // Only the source's own outgoing delegation observation exposes its
            // participant before handling completes and reinforces a Learned edge.
            foreach (var delivery in read.Outgoing.Delta)
            {
                if (VisibleDelivery(delivery, privateNeuron, actor.PrincipalId, observeShared)
                    && delivery.Signal is AgentActivity { Kind: "delegation", Target: { } target })
                {
                    Discover(target);
                }
            }
        }

        var activity = new List<BrainGraphActivity>();
        var nodes = new List<BrainGraphNode>();
        var synapses = new List<BrainGraphSynapse>();
        foreach (var (neuron, read) in reads)
        {
            var privateNeuron = IsPrivate(neuron, actor.PrincipalId, activeExecution);
            var observeShared = ObservesSharedComposition(neuron);
            var neuronActivity = ProjectActivity(neuron, read.Incoming, JournalKind.Incoming, privateNeuron, actor.PrincipalId, observeShared)
                .Concat(ProjectActivity(neuron, read.Outgoing, JournalKind.Outgoing, privateNeuron, actor.PrincipalId, observeShared))
                .OrderBy(item => item.Timestamp).ToArray();
            activity.AddRange(neuronActivity);
            var metadata = _metadata.For(neuron.Type);
            var localName = PrincipalPartition.TryParse(neuron.Name, out _, out var local) ? local : neuron.Name;
            var lastStatus = Status(read.Outgoing.Delta
                .Where(delivery => VisibleDelivery(delivery, privateNeuron, actor.PrincipalId)));
            nodes.Add(new(InstanceId(neuron), neuron.Type, localName, metadata.Label, metadata.Module,
                participants.Contains(neuron) ? "participant" : "observed",
                unavailable.Contains(neuron) ? "Unavailable" : lastStatus, metadata.HandledSignals,
                read.Incoming.ResumeSequence, read.Outgoing.ResumeSequence,
                neuronActivity.LastOrDefault()?.Timestamp, metadata.IconKey,
                IsInfrastructureType(neuron.Type)));

            foreach (var edge in read.Synapses)
            {
                if (edge.Source != neuron || !reads.ContainsKey(edge.Target)
                    || !CanSee(edge.Target, actor.PrincipalId)
                    || (!privateNeuron && !observeShared
                        && !IsPrivate(edge.Target, actor.PrincipalId, activeExecution)))
                {
                    continue;
                }

                synapses.Add(new(SynapseId(edge), InstanceId(edge.Source), InstanceId(edge.Target), edge.SignalType,
                    edge.Kind.ToString(), edge.Weight, edge.FireCount, edge.LastFiredAt, edge.IsBlocking,
                    edge.Kind == SynapseKind.Bound
                        && PrincipalPartition.OwnsInstance(actor.PrincipalId, edge.Target.Name)
                        && BrainGraphMetadata.IsSubscriptionSignal(edge.SignalType)));
            }
        }

        // A target can be observed before its first handler journal entry. This
        // changes only its observed status, never the authoritative synapse list.
        var activeTargets = activity.Where(item => item.Kind == "delegation" && item.OperationId is not null)
            .GroupBy(item => (item.NeuronId, item.OperationId))
            .Select(group => group.OrderBy(item => item.Timestamp).ThenBy(item => item.Sequence).Last())
            .Where(item => item.State == "started" && item.TargetId is not null)
            .Select(item => item.TargetId).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].Status == "Idle" && activeTargets.Contains(nodes[index].Id))
            {
                nodes[index] = nodes[index] with { Status = "Running" };
            }
        }

        var correlations = GroupCorrelations(activity);
        truncated |= correlations.Count > MaxActivity || reads.Values.Any(read =>
            read.Incoming.ResetSnapshot is not null || read.Outgoing.ResetSnapshot is not null);
        var selected = correlations.Take(MaxActivity).ToArray();
        var selectedIds = selected.Select(item => item.CorrelationId).ToHashSet(StringComparer.Ordinal);
        var selectedActivity = activity
            .Where(item => selectedIds.Contains(item.CorrelationId)
                || item.Kind == "historical"
                || item.SignalType is "Subscribe" or "Unsubscribe" or "DigitalBrainActivated")
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
        return new(InstanceId(assistant), DateTimeOffset.UtcNow, truncated, SnapshotScope,
            nodes, synapses, selectedActivity, selected);

        void Discover(NeuronId candidate)
        {
            if (!CanSee(candidate, actor.PrincipalId) || known.Contains(candidate))
            {
                return;
            }
            known.Add(candidate);
            pending.Enqueue(candidate);
        }
    }

    public async Task<BrainGraphSubscriptionResult> SetSubscriptionAsync(
        string chatName, ActorContext actor, BrainGraphSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!NeuronId.TryParseInstance(request.SourceId, source.Owner, out var from)
            || !NeuronId.TryParseInstance(request.TargetId, source.Owner, out var to)
            || from == to
            || !CanSee(from, actor.PrincipalId)
            || !PrincipalPartition.OwnsInstance(actor.PrincipalId, to.Name)
            || !BrainGraphMetadata.IsSubscriptionSignal(request.SignalType))
        {
            throw new NeuronAuthorizationException("This subscription is outside the current conversation's graph scope.");
        }

        // Rebuild scope immediately before mutation. Never trust a client's stale graph,
        // its claimed edge kind, or an arbitrary owner/instance encoded in the request.
        var snapshot = await ReadAsync(chatName, actor, cancellationToken).ConfigureAwait(false);
        var target = snapshot.Nodes.FirstOrDefault(node => node.Id == InstanceId(to));
        if (!snapshot.Nodes.Any(node => node.Id == InstanceId(from))
            || target is null
            || !target.HandledSignals.Contains(request.SignalType, StringComparer.Ordinal))
        {
            throw new NeuronAuthorizationException("The target does not handle this signal in the current graph scope.");
        }

        var existing = snapshot.Synapses.FirstOrDefault(edge => edge.SourceId == InstanceId(from)
            && edge.TargetId == InstanceId(to) && edge.SignalType == request.SignalType);
        if (existing?.Kind == nameof(SynapseKind.Innate))
        {
            throw new NeuronAuthorizationException("Innate connections cannot be changed from the graph.");
        }

        if (!request.Subscribed && existing is not null && !existing.CanUnsubscribe)
        {
            throw new NeuronAuthorizationException("Only explicit Bound subscriptions can be removed here.");
        }

        if (request.Subscribed || existing is not null)
        {
            using var principal = VerifiedActor.Enter(actor);
            Signal signal = request.Subscribed
                ? new Subscribe(from, request.SignalType)
                : new Unsubscribe(from, request.SignalType);
            var outcome = await source.SendAsync(to, signal, cancellationToken).ConfigureAwait(false);
            if (outcome != DeliveryOutcome.Handled)
            {
                throw new NeuronAuthorizationException("The neuron did not accept the subscription change.");
            }
        }

        return new(InstanceId(from), InstanceId(to), request.SignalType, request.Subscribed);
    }

    internal static string InstanceId(NeuronId neuron) => $"{neuron.Type}:{neuron.Name}";

    private bool CanSee(NeuronId neuron, PrincipalId principal)
        => neuron.Owner == source.Owner
            && (!PrincipalPartition.TryParse(neuron.Name, out var other, out _) || other == principal);

    private static bool IsPrivate(NeuronId neuron, PrincipalId principal, NeuronId? execution)
        => PrincipalPartition.OwnsInstance(principal, neuron.Name) || neuron == execution;

    private static bool IsInfrastructureType(string type)
        => type is "chat-turn-worker" or "sessionneuron" or "execution"
            or "usermessages" or "surface-boot" or "uirenderer" or "chat";

    private static bool ObservesSharedComposition(NeuronId neuron)
        => neuron.Type is "usermessages" or "sessionneuron" or "activities" or "activitysource" or "uirenderer";

    private static bool VisibleDelivery(
        SignalDelivery delivery, bool privateNeuron, PrincipalId principal, bool observeShared = false)
        => !delivery.IsActivityTelemetry
            && (delivery.Principal == principal || ((privateNeuron || observeShared) && delivery.Principal is null));

    private bool VisibleCaller(NeuronId caller, PrincipalId principal) => CanSee(caller, principal);

    private IEnumerable<BrainGraphActivity> ProjectActivity(
        NeuronId neuron, JournalRead journal, JournalKind direction, bool privateNeuron, PrincipalId principal,
        bool observeShared = false)
    {
        for (var index = 0; index < journal.Delta.Count; index++)
        {
            var delivery = journal.Delta[index];
            if (!VisibleDelivery(delivery, privateNeuron, principal, observeShared))
            {
                continue;
            }
            var sequence = journal.SequenceOf(index);
            var type = delivery.Signal.GetType().Name;
            var (summary, preview) = Summarize(delivery.Signal);
            var operation = direction == JournalKind.Outgoing ? delivery.Signal as AgentActivity : null;
            var visibleTarget = operation?.Target is { } target && CanSee(target, principal)
                ? InstanceId(target) : null;
            yield return new($"{InstanceId(neuron)}:{direction}:{sequence}", InstanceId(neuron), direction.ToString(),
                sequence, type, delivery.Timestamp,
                VisibleCaller(delivery.Caller, principal) ? InstanceId(delivery.Caller) : "",
                delivery.CorrelationId.ToString(), summary, preview,
                operation?.OperationId, operation?.Kind, operation?.State, operation?.Name,
                visibleTarget, operation?.Server, operation?.DurationMs,
                operation?.Kind == "tool" ? BoundPreview(operation.Preview) : null,
                operation?.IsError == true,
                operation?.Truncated == true || operation?.Kind == "tool" && operation.Preview?.Length > 4096,
                SafeFailureCode(operation?.FailureCode));
        }

        // An unreadable historical envelope cannot establish a principal, caller,
        // timestamp, or signal. Only an independently scoped private journal may
        // expose its existence; shared journals must keep those entries private.
        if (privateNeuron)
        {
            foreach (var entry in journal.UnknownEntries ?? [])
            {
                yield return new($"{InstanceId(neuron)}:{direction}:{entry.Sequence}",
                    InstanceId(neuron), direction.ToString(), entry.Sequence,
                    "Unavailable history", null, "", "",
                    "Historical event unavailable because its type is no longer installed.",
                    null, Kind: "historical", State: "unavailable");
            }
        }
    }

    private static string? SafeFailureCode(string? code) => code is
        "unavailable" or "catalog_changed" or "connection_changed" or "access_denied"
        or "content_rejected" or "capacity" or "timeout" or "authentication_required" or "cancelled"
            ? code : null;

    // Explicit allowlist: never serialize arbitrary Signal objects, tool credentials,
    // prompt text, document contents, OAuth URLs, or exception details into the graph.
    internal static (string Summary, IReadOnlyDictionary<string, string>? Preview) Summarize(Signal signal)
        => signal switch
        {
            AgentActivity activity => ($"{activity.Kind}: {activity.Name} · {activity.State}", null),
            AgentRequest request => ("Agent request received", new Dictionary<string, string>
            { ["characters"] = request.Text.Length.ToString(CultureInfo.InvariantCulture) }),
            AgentReply reply => ("Agent reply recorded", new Dictionary<string, string>
            { ["characters"] = reply.Text.Length.ToString(CultureInfo.InvariantCulture) }),
            TurnLifecycle turn => ($"Turn {turn.Status.ToString().ToLowerInvariant()}",
                new Dictionary<string, string> { ["status"] = turn.Status.ToString(), ["turnId"] = turn.TurnId.ToString() }),
            UserMessaged message => ("Message received", new Dictionary<string, string>
            { ["characters"] = message.Text.Length.ToString(CultureInfo.InvariantCulture) }),
            DigitalBrain.Memory.MemoryUpdated updated => ("Memory updated", new Dictionary<string, string>
            { ["key"] = updated.Key }),
            Responded response => ("Assistant response recorded", new Dictionary<string, string>
            { ["characters"] = response.Text.Length.ToString(CultureInfo.InvariantCulture) }),
            Subscribe subscription => ("Subscription bound", new Dictionary<string, string>
            { ["signalType"] = subscription.SignalType }),
            Unsubscribe subscription => ("Subscription removed", new Dictionary<string, string>
            { ["signalType"] = subscription.SignalType }),
            DigitalBrainActivated => ("DigitalBrain activated", null),
            _ => ($"{signal.GetType().Name} observed · payload omitted", null),
        };

    internal static IReadOnlyList<BrainCorrelation> GroupCorrelations(
        IReadOnlyList<BrainGraphActivity> activity)
    {
        return [.. activity
            .Where(item => item.CorrelationId.Length > 0)
            .GroupBy(item => item.CorrelationId, StringComparer.Ordinal)
            .Select(group =>
            {
                var items = group.OrderBy(item => item.Timestamp).ThenBy(item => item.Sequence).ToArray();
                var last = items.Last();
                return new BrainCorrelation(
                    group.Key,
                    CorrelationStatus(items),
                    last.Summary,
                    last.Timestamp,
                    [.. items.Select(item => item.NeuronId).Distinct(StringComparer.Ordinal)],
                    items.Length);
            })
            .OrderByDescending(item => item.LastAt)];
    }

    private static string CorrelationStatus(IReadOnlyList<BrainGraphActivity> items)
    {
        if (items.Any(item => item.IsError
            || string.Equals(item.State, "failed", StringComparison.Ordinal)
            || TurnStatus(item) is "Failed"))
        {
            return "failed";
        }

        if (items.Any(item => TurnStatus(item) is "WaitingForUser")
            || items.Any(item => string.Equals(item.FailureCode, "authentication_required", StringComparison.Ordinal)))
        {
            return "needs-approval";
        }

        if (items.Any(item => string.Equals(item.State, "started", StringComparison.Ordinal))
            && items.GroupBy(item => item.OperationId)
                .Any(operation => operation.Key is not null
                    && string.Equals(operation.Last().State, "started", StringComparison.Ordinal))
            || TurnStatus(items.Last()) is "Running" or "Pending" or "Cancelling")
        {
            return "live";
        }

        return "completed";
    }

    private static string? TurnStatus(BrainGraphActivity item)
        => item.SignalType == nameof(TurnLifecycle)
            && item.PayloadPreview is { } preview
            && preview.TryGetValue("status", out var status)
                ? status
                : null;

    private static string Status(IEnumerable<SignalDelivery> deliveries)
    {
        var outgoing = deliveries.OrderBy(delivery => delivery.Timestamp).ToArray();
        var operations = outgoing.Select(delivery => delivery.Signal).OfType<AgentActivity>().ToArray();
        var agentOperations = operations.Where(operation => operation.Kind == "agent").ToArray();
        var statusOperations = agentOperations.Length > 0 ? agentOperations : operations;
        // Nested tools completing must not mark an agent idle while its outer turn
        // is still running. Every operation has its own start/terminal identity.
        if (statusOperations.GroupBy(operation => operation.OperationId).Any(group => group.Last().State == "started"))
        {
            return "Running";
        }
        Signal? lastStatus = agentOperations.LastOrDefault() ??
            outgoing.LastOrDefault(delivery => delivery.Signal is TurnLifecycle or AgentActivity)?.Signal;
        return lastStatus switch
        {
            TurnLifecycle turn => turn.Status.ToString(),
            AgentActivity { State: "failed" } => "Failed",
            AgentActivity { State: "cancelled" } => "Cancelled",
            _ => "Idle",
        };
    }

    // Only the shared MCP boundary supplies this screened result. A second bound
    // protects graph response size; arbitrary AgentRequest/Reply bodies stay out.
    private static string? BoundPreview(string? preview)
        => preview is null or { Length: 0 } ? null
            : preview.Length <= 4096 ? preview : preview[..4096] + "\n[Graph preview truncated]";

    private static string SynapseId(Synapse edge)
        => $"{InstanceId(edge.Source)}|{edge.SignalType}|{InstanceId(edge.Target)}";
}
