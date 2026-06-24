namespace DigitalBrain.Protocol;

public interface INeuron : IGrainWithStringKey
{
    ValueTask FireAsync<T>(T payload) where T : Synapse;
    Task<IReadOnlyList<Synapse>> GetTimelineAsync();
    Task DeliverAsync(Synapse synapse);

    // Dual journal accessors (outgoing is primary causal log of actions taken by this neuron).
    Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync();
    Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync();

    // OS kernel simulation/time-travel primitives.
    ValueTask<Checkpoint> CreateCheckpointAsync();
    Task<NeuronId> BranchAsync(Checkpoint checkpoint);
    Task RestoreCheckpointAsync(Checkpoint checkpoint);
}
