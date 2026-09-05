using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Trusted scripting-host protocol. User programs use ordinary signal requests.
[Alias("db.behavior-kernel")]
public interface IBehaviorKernel : IGrainWithStringKey
{
    [ReadOnly, AlwaysInterleave]
    Task<BehaviorView> ReadState();
    Task ValidateDraft(Guid revision, string[] diagnostics, string[]? inputSignalTypes = null, string[]? outputSignalTypes = null, string? runtimeFingerprint = null);
    Task<BehaviorClaim?> TryClaim();
    Task<bool> Renew(BehaviorClaim claim);
    Task<BehaviorCheckpoint?> ReadCheckpoint(BehaviorClaim claim, string key, string requestHash);
    Task<SignalDelivery> PrepareRequest(BehaviorClaim claim, string key, string requestHash, NeuronId receiver, Signal request);
    Task CompleteRequest(BehaviorClaim claim, string key, string requestHash);
    Task StoreCheckpoint(BehaviorClaim claim, BehaviorCheckpoint checkpoint);
    Task Complete(BehaviorClaim claim, Signal? output);
    Task Fail(BehaviorClaim claim, string detail, bool retryable);
}
