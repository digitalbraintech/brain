using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed partial class ApplicationCatalog
{
    private async Task<Guid[]> CatchUpRootActivation(OwnerId owner, string ownerApplication,
        ApplicationManifest manifest, string? targetApplication = null)
    {
        var rootId = IBrainNeuron.ForOwner(owner);
        var connections = (manifest.Connections ?? []).Where(connection =>
            connection.Source.SourceKind == "neuron"
            && connection.Source.SourceId == rootId.ToString()
            && connection.Source.BehaviorKey.Length == 0
            && connection.Source.Key == "activated"
            && connection.Source.Contract == "brain.activated/v1"
            && (targetApplication is null || connection.TargetApplicationKey == targetApplication)
            && !storage.State.RootActivationConnections.ContainsKey(
                $"{ownerApplication}/{connection.Key}/{connection.TargetApplicationKey}/{connection.TargetOperation}"))
            .ToArray();
        if (connections.Length == 0) { return []; }
        var journal = await grains.GetGrain<INeuronQuery>(rootId.ToGrainId()).ReadJournal(JournalKind.Outgoing, 0);
        var operations = new List<Guid>();
        foreach (var delivery in journal.Delta.Where(x => x.Signal is DigitalBrainActivated))
        {
            var payload = JsonSerializer.Serialize((DigitalBrainActivated)delivery.Signal);
            var stored = new Dictionary<Guid, ApplicationStoredDelivery>(storage.State.EventDeliveries);
            var completed = new Dictionary<string, bool>(storage.State.RootActivationConnections);
            foreach (var group in connections.GroupBy(connection =>
                         (connection.TargetApplicationKey, connection.TargetOperation, connection.Contract)))
            {
                var target = storage.State.Heads[group.Key.TargetApplicationKey];
                var deliveryId = StableId(delivery.SignalId.Value, group.Key.TargetApplicationKey,
                    group.Key.TargetOperation);
                operations.Add(deliveryId);
                stored.TryAdd(deliveryId, new(deliveryId, deliveryId, group.Key.TargetApplicationKey,
                    target.Revision, group.Key.TargetOperation, group.Key.Contract, payload,
                    ConnectionOwners: group.Select(x => $"{ownerApplication}/{x.Key}")
                        .Order(StringComparer.Ordinal).ToArray()));
                foreach (var connection in group)
                {
                    completed[$"{ownerApplication}/{connection.Key}/{connection.TargetApplicationKey}/{connection.TargetOperation}"] = true;
                }
            }
            if (stored.Count != storage.State.EventDeliveries.Count
                || completed.Count != storage.State.RootActivationConnections.Count)
            {
                await Commit(storage.State with
                {
                    EventDeliveries = stored,
                    RootActivationConnections = completed,
                });
            }
        }
        return operations.Distinct().ToArray();
    }
}
