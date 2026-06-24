namespace DigitalBrain.Protocol;

// An encrypted checkpoint: the (Orleans-serialized) synapse snapshot protected at rest.
// Round-trips to/from a Checkpoint via CheckpointProtector.
[GenerateSerializer]
public record ProtectedCheckpoint(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] byte[] EncryptedSnapshot,
    [property: Id(2)] DateTimeOffset TakenAt);
