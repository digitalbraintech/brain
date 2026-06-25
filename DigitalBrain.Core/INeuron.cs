namespace DigitalBrain.Core;

public interface INeuron : IGrainWithStringKey
{
    ValueTask FireAsync<T>(T payload) where T : Synapse;
    Task<IReadOnlyList<Synapse>> GetTimelineAsync();
    Task DeliverAsync(Synapse synapse);

    // Dual journal accessors (outgoing is primary causal log of actions taken by this neuron).
    Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync();
    Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync();

    // Causal query APIs for lineage traversal without reimplementing in callers (UI, debug, MCP).
    Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId);
    Task<IReadOnlyList<Synapse>> GetTimelineForCorrelationAsync(string correlationId);

    // OS kernel simulation/time-travel primitives.
    ValueTask<Checkpoint> CreateCheckpointAsync();
    Task<NeuronId> BranchAsync(Checkpoint checkpoint);
    Task RestoreCheckpointAsync(Checkpoint checkpoint);
}
