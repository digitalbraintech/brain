using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Core;

internal sealed partial class ApplicationCatalog
{
    private async Task CatchUpNeuronEvents(OwnerId owner, string ownerApplication,
        ApplicationManifest manifest, string targetApplication)
    {
        var principal = this.GetPrimaryKeyString().Split('/')[1];
        var connections = (manifest.Connections ?? []).Where(connection =>
            (connection.TargetNeuronId is not null
                ? ownerApplication == targetApplication
                : connection.TargetApplicationKey == targetApplication)
            && TryResolveNeuronEvent(owner, connection.Source, out _, out var signalType)
            && signalType != typeof(DigitalBrain.Abstractions.Signals.DigitalBrainActivated)).ToArray();
        foreach (var sourceGroup in connections.GroupBy(x => x.Source.SourceId))
        {
            _ = TryResolveNeuronEvent(owner, sourceGroup.First().Source, out var sourceId, out var signalType);
            foreach (var connection in sourceGroup)
            {
                var cursorKey = CursorKey(ownerApplication, connection);
                var after = storage.State.EventSourceCursors.GetValueOrDefault(cursorKey);
                var journal = await grains.GetGrain<INeuronQuery>(sourceId.ToGrainId())
                    .ReadJournal(JournalKind.Outgoing, after);
                var deliveries = new Dictionary<Guid, ApplicationStoredDelivery>(storage.State.EventDeliveries);
                foreach (var observed in journal.Delta.Where(x =>
                    x.Signal.GetType() == signalType
                    && x.Principal?.ToString() == principal))
                {
                    var deliveryApplication = connection.TargetNeuronId is null
                        ? connection.TargetApplicationKey : ownerApplication;
                    var deliveryOperation = connection.TargetNeuronId is null
                        ? connection.TargetOperation : connection.Key;
                    var id = StableId(observed.SignalId.Value, deliveryApplication, deliveryOperation);
                    var target = storage.State.Heads[deliveryApplication];
                    deliveries.TryAdd(id, new(id, id, deliveryApplication, target.Revision,
                        deliveryOperation, connection.Contract,
                        JsonSerializer.Serialize(observed.Signal, observed.Signal.GetType()),
                        ConnectionOwners: [$"{ownerApplication}/{connection.Key}"],
                        TargetNeuronId: connection.TargetNeuronId, ObservedDelivery: observed));
                }
                if (journal.ResumeSequence > after)
                {
                    await Commit(storage.State with
                    {
                        EventDeliveries = deliveries,
                        EventSourceCursors = new(storage.State.EventSourceCursors)
                        {
                            [cursorKey] = journal.ResumeSequence,
                        },
                    });
                }
            }
        }
    }

    private async Task<Dictionary<string, long>> InitializeEventCursors(OwnerId owner,
        string ownerApplication, ApplicationManifest manifest)
    {
        var cursors = new Dictionary<string, long>(storage.State.EventSourceCursors);
        foreach (var connection in manifest.Connections ?? [])
        {
            if (!TryResolveNeuronEvent(owner, connection.Source, out var source, out var signalType)
                || signalType == typeof(DigitalBrain.Abstractions.Signals.DigitalBrainActivated))
            {
                continue;
            }
            var key = CursorKey(ownerApplication, connection);
            if (cursors.ContainsKey(key)) { continue; }
            var tail = await grains.GetGrain<INeuronQuery>(source.ToGrainId())
                .ReadJournal(JournalKind.Outgoing, long.MaxValue);
            cursors[key] = tail.ResumeSequence;
        }
        return cursors;
    }
}
