using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed partial class ApplicationCatalog
{
    public async Task<ApplicationPublication> Publish(ApplicationEventEmission emission)
    {
        var owner = Authorize();
        if (emission.EventId == Guid.Empty || emission.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("An application event needs event and correlation identities.");
        }
        if (emission.SourceKind != "application")
        {
            throw new InvalidOperationException("This catalog slice supports application-owned event sources only.");
        }
        var revisionKey = $"{emission.SourceId}/{emission.SourceRevision}";
        var declared = new ApplicationEventOutput(emission.SourceKind, emission.SourceId,
            emission.BehaviorKey, emission.OutputKey, emission.Contract);
        if (!storage.State.Revisions.TryGetValue(revisionKey, out var sourceManifest)
            || !(sourceManifest.Outputs ?? []).Contains(declared))
        {
            throw new InvalidOperationException("The pinned application revision does not declare this event output.");
        }

        var hash = Fingerprint(emission.CorrelationId.ToString("N"), emission.SourceKind, emission.SourceId,
            emission.SourceRevision, emission.BehaviorKey, emission.OutputKey, emission.Contract, emission.Payload);
        if (storage.State.Events.TryGetValue(emission.EventId, out var existing))
        {
            if (existing.Hash != hash)
            {
                throw new InvalidOperationException("An event identity cannot be reused with different content.");
            }
            return new(emission.EventId, existing.RecipientCount);
        }
        var matches = storage.State.Heads.SelectMany(head => (head.Value.Connections ?? [])
                .Select(connection => (Owner: head.Key, Connection: connection)))
            .Where(candidate => candidate.Connection.Source == declared)
            .GroupBy(candidate => candidate.Connection.TargetNeuronId is { } targetNeuron
                ? (Application: emission.SourceId, Operation: targetNeuron,
                    candidate.Connection.Contract, TargetNeuronId: targetNeuron)
                : (Application: candidate.Connection.TargetApplicationKey,
                    Operation: candidate.Connection.TargetOperation,
                    candidate.Connection.Contract, TargetNeuronId: null))
            .Select(group => new
            {
                Target = group.Key,
                Owners = group.Select(candidate => $"{candidate.Owner}/{candidate.Connection.Key}")
                    .Order(StringComparer.Ordinal).ToArray(),
            }).ToArray();
        var deliveries = new Dictionary<Guid, ApplicationStoredDelivery>(storage.State.EventDeliveries);
        foreach (var match in matches)
        {
            var revision = match.Target.TargetNeuronId is null
                ? storage.State.Heads[match.Target.Application].Revision
                : emission.SourceRevision;
            SignalDelivery? observed = null;
            if (match.Target.TargetNeuronId is { } targetValue)
            {
                observed = await PrepareApplicationEventDelivery(
                    owner, targetValue, match.Target.Contract, emission);
            }
            var deliveryId = StableId(emission.EventId, match.Target.Application,
                $"{match.Target.Operation}/{match.Target.Contract}");
            deliveries[deliveryId] = new(deliveryId, deliveryId, match.Target.Application,
                revision, match.Target.Operation, match.Target.Contract, emission.Payload,
                ConnectionOwners: match.Owners, TargetNeuronId: match.Target.TargetNeuronId,
                ObservedDelivery: observed);
        }
        await Commit(storage.State with
        {
            Events = new(storage.State.Events)
            {
                [emission.EventId] = new(hash, matches.Length,
                    checked(storage.State.NextApplicationEventSequence + 1), emission),
            },
            EventDeliveries = deliveries,
            NextApplicationEventSequence = checked(storage.State.NextApplicationEventSequence + 1),
        });
        return new(emission.EventId, matches.Length);
    }

    public Task<long> EventCursor(ApplicationEventOutput source)
    {
        Authorize();
        ValidateWaitSource(source);
        return Task.FromResult(storage.State.NextApplicationEventSequence);
    }

    public Task<string?> ReadEventAfter(ApplicationEventOutput source, long afterSequence)
    {
        Authorize();
        ValidateWaitSource(source);
        var emission = storage.State.Events.Values
            .Where(stored => stored.Sequence > afterSequence && stored.Emission is not null)
            .OrderBy(stored => stored.Sequence)
            .Select(stored => stored.Emission!)
            .FirstOrDefault(candidate => candidate.SourceKind == source.SourceKind &&
                candidate.SourceId == source.SourceId && candidate.BehaviorKey == source.BehaviorKey &&
                candidate.OutputKey == source.Key && candidate.Contract == source.Contract);
        return Task.FromResult(emission?.Payload);
    }

    private void ValidateWaitSource(ApplicationEventOutput source)
    {
        if (source.SourceKind != "application" ||
            !storage.State.Revisions.Values.Any(manifest => (manifest.Outputs ?? []).Contains(source)))
        {
            throw new InvalidOperationException("An event wait source is not a declared application output.");
        }
    }

    public async Task<ApplicationEventDelivery?> ClaimDelivery(string applicationKey, string revision)
    {
        var owner = Authorize();
        foreach (var head in storage.State.Heads)
        {
            await CatchUpRootActivation(owner, head.Key, head.Value, applicationKey);
            await CatchUpNeuronEvents(owner, head.Key, head.Value, applicationKey);
        }
        var now = DateTimeOffset.UtcNow;
        var pending = storage.State.EventDeliveries.Values.FirstOrDefault(x => !x.Acknowledged
            && x.ApplicationKey == applicationKey && x.Revision == revision
            && (x.Lease is null || x.LeaseExpiresAt is null || x.LeaseExpiresAt.Value <= now));
        if (pending is null)
        {
            return null;
        }
        var lease = Guid.NewGuid();
        var leased = pending with { Lease = lease, LeaseExpiresAt = now.AddSeconds(30) };
        await Commit(storage.State with
        {
            EventDeliveries = new(storage.State.EventDeliveries) { [pending.DeliveryId] = leased },
        });
        return new(leased.DeliveryId, leased.OperationId, lease, leased.ApplicationKey, leased.Revision,
            leased.Operation, leased.Contract, leased.Payload, leased.TargetNeuronId, leased.ObservedDelivery);
    }

    public async Task AckDelivery(Guid deliveryId, Guid lease)
    {
        Authorize();
        if (!storage.State.EventDeliveries.TryGetValue(deliveryId, out var delivery) || delivery.Lease != lease)
        {
            throw new InvalidOperationException("The event delivery lease is no longer valid.");
        }
        await Commit(storage.State with
        {
            EventDeliveries = new(storage.State.EventDeliveries)
            {
                [deliveryId] = delivery with { Acknowledged = true, Lease = null, LeaseExpiresAt = null },
            },
        });
    }

    public Task<string[]> PendingDeliveryRevisions(string applicationKey)
    {
        Authorize();
        return Task.FromResult(storage.State.EventDeliveries.Values
            .Where(delivery => !delivery.Acknowledged && delivery.ApplicationKey == applicationKey)
            .Select(delivery => delivery.Revision)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    private void ValidateEvents(string applicationKey, ApplicationManifest manifest,
        IReadOnlyDictionary<string, ApplicationManifest> heads)
    {
        var outputs = manifest.Outputs ?? [];
        if (outputs.Any(x => x.SourceKind != "application" || x.SourceId != applicationKey)
            || outputs.GroupBy(x => (x.BehaviorKey, x.Key)).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException("Event outputs require unique keys owned by their definition.");
        }
        var connections = manifest.Connections ?? [];
        if (connections.GroupBy(x => x.Key, StringComparer.Ordinal).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException("Connection keys must be unique within their definition.");
        }
        foreach (var connection in connections)
        {
            var targetExists = connection.TargetNeuronId is { } targetNeuron
                ? TryResolveNeuronInput(new OwnerId(this.GetPrimaryKeyString().Split('/')[0]),
                    targetNeuron, connection.Contract, out _)
                : heads.TryGetValue(connection.TargetApplicationKey, out var target)
                    && target.Operations.Any(x => x.Key == connection.TargetOperation
                        && x.RequestContract == connection.Contract && x.ResponseContract == "system.boolean/v1");
            var sourceExists = connection.Source.SourceKind == "application"
                ? heads.TryGetValue(connection.Source.SourceId, out var source)
                    && (source.Outputs ?? []).Contains(connection.Source)
                : TryResolveNeuronEvent(new OwnerId(this.GetPrimaryKeyString().Split('/')[0]),
                    connection.Source, out _, out _);
            if (connection.Source.Contract != connection.Contract || !targetExists || !sourceExists)
            {
                throw new InvalidOperationException("A connection must join matching installed typed ports.");
            }
        }
    }

    private bool TryResolveNeuronEvent(OwnerId owner, ApplicationEventOutput source,
        out NeuronId neuron, out Type signalType)
    {
        neuron = default;
        signalType = null!;
        if (source.SourceKind != "neuron" || source.BehaviorKey.Length != 0) { return false; }
        if (source.SourceId == IBrainNeuron.ForOwner(owner).ToString()
            && source.Key == "activated" && source.Contract == "brain.activated/v1")
        {
            neuron = IBrainNeuron.ForOwner(owner);
            signalType = typeof(DigitalBrain.Abstractions.Signals.DigitalBrainActivated);
            return true;
        }
        var registration = eventSources.SingleOrDefault(candidate => candidate.EventKey == source.Key
            && candidate.Contract == source.Contract
            && source.SourceId.StartsWith($"{candidate.NeuronType}:{owner.Value}/", StringComparison.Ordinal));
        if (registration is null) { return false; }
        var prefix = $"{registration.NeuronType}:{owner.Value}/";
        if (source.SourceId.Length == prefix.Length) { return false; }
        neuron = new(registration.NeuronType, owner, source.SourceId[prefix.Length..]);
        signalType = registration.SignalType;
        return neuron.ToString() == source.SourceId;
    }

    private static string CursorKey(string ownerApplication, ApplicationEventConnection connection)
        => $"{ownerApplication}/{connection.Key}/{connection.Source.SourceKind}/{connection.Source.SourceId}/" +
            $"{connection.Source.Key}/{connection.TargetApplicationKey}/{connection.TargetOperation}/{connection.TargetNeuronId}";

    private bool TryResolveNeuronInput(OwnerId owner, string value, string contract, out NeuronId neuron)
        => TryResolveNeuronInput(owner, value, contract, out neuron, out _);

    private bool TryResolveNeuronInput(
        OwnerId owner,
        string value,
        string contract,
        out NeuronId neuron,
        out Type signalType)
    {
        neuron = default;
        signalType = null!;
        var registration = neuronInputs.SingleOrDefault(candidate => candidate.Contract == contract
            && value.StartsWith($"{candidate.NeuronType}:{owner.Value}/", StringComparison.Ordinal));
        if (registration is null) { return false; }
        var prefix = $"{registration.NeuronType}:{owner.Value}/";
        if (value.Length == prefix.Length) { return false; }
        neuron = new(registration.NeuronType, owner, value[prefix.Length..]);
        if (neuron.ToString() != value) { return false; }
        signalType = registration.SignalType;
        return true;
    }

    private async Task<SignalDelivery> PrepareApplicationEventDelivery(
        OwnerId owner,
        string targetValue,
        string contract,
        ApplicationEventEmission emission)
    {
        if (!TryResolveNeuronInput(owner, targetValue, contract, out _, out var signalType)
            || JsonSerializer.Deserialize(emission.Payload, signalType) is not Signal signal)
        {
            throw new InvalidOperationException("An application event payload does not match its neuron input contract.");
        }
        var principal = new PrincipalId(Guid.Parse(this.GetPrimaryKeyString().Split('/')[1]));
        var sourceId = new NeuronId("application-call-source", owner,
            PrincipalPartition.InstanceName(principal, $"{emission.SourceId}.{emission.EventId:N}"));
        return await grains.GetGrain<IApplicationCallSource>(sourceId.ToGrainId())
            .Prepare(signal, new SignalId(emission.EventId), new CorrelationId(emission.CorrelationId), sourceEpoch: 1);
    }

    private static string Fingerprint(params string[] values)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static Guid StableId(Guid eventId, string targetApplication, string targetOperation)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { eventId.ToString("N"), targetApplication, targetOperation })))[..16]);
}

[GenerateSerializer, Alias("db.application-stored-event")]
internal sealed record ApplicationStoredEvent(
    [property: Id(0)] string Hash,
    [property: Id(1)] int RecipientCount,
    [property: Id(2)] long Sequence = 0,
    [property: Id(3)] ApplicationEventEmission? Emission = null);

[GenerateSerializer, Alias("db.application-stored-delivery")]
internal sealed record ApplicationStoredDelivery(
    [property: Id(0)] Guid DeliveryId, [property: Id(1)] Guid OperationId,
    [property: Id(2)] string ApplicationKey, [property: Id(3)] string Revision,
    [property: Id(4)] string Operation, [property: Id(5)] string Contract,
    [property: Id(6)] string Payload, [property: Id(7)] Guid? Lease = null,
    [property: Id(8)] DateTimeOffset? LeaseExpiresAt = null, [property: Id(9)] bool Acknowledged = false,
    [property: Id(10)] string[]? ConnectionOwners = null,
    [property: Id(11)] string? TargetNeuronId = null,
    [property: Id(12)] SignalDelivery? ObservedDelivery = null);
