using DigitalBrain.Protocol;
using Orleans.Serialization;

namespace DigitalBrain.Silo;

// Encrypts/decrypts a kernel Checkpoint at rest. The polymorphic Synapse snapshot is serialized with the Orleans
// serializer (which handles every [GenerateSerializer] subtype) and then protected via INeuronStateProtector.
public sealed class CheckpointProtector(Serializer serializer, INeuronStateProtector protector)
{
    public ProtectedCheckpoint Protect(Checkpoint checkpoint)
    {
        var bytes = serializer.SerializeToArray(checkpoint.Snapshot.ToList());
        return new ProtectedCheckpoint(checkpoint.Source, protector.Protect(bytes), checkpoint.TakenAt);
    }

    public Checkpoint Unprotect(ProtectedCheckpoint protectedCheckpoint)
    {
        var bytes = protector.Unprotect(protectedCheckpoint.EncryptedSnapshot);
        var snapshot = serializer.Deserialize<List<Synapse>>(bytes);
        return new Checkpoint(protectedCheckpoint.Source, snapshot.AsReadOnly(), protectedCheckpoint.TakenAt);
    }
}
