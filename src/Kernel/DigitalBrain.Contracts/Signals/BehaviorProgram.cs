using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer, Alias("db.behavior-validation")]
public enum BehaviorValidation { Pending, Valid, Invalid }

[Flags, GenerateSerializer, Alias("db.behavior-input-policy")]
public enum BehaviorInputPolicy
{
    EveryEvent = 0,
    LatestPerSubject = 1,
    ObserveFromActivation = 2,
    OncePerVersion = 4,
}

[GenerateSerializer, Alias("db.behavior-program")]
public sealed record BehaviorProgram(
    [property: Id(0)] Guid Revision,
    [property: Id(1)] string Source,
    [property: Id(2)] string[] InputSignalTypes,
    [property: Id(3)] string[] OutputSignalTypes,
    [property: Id(4)] BehaviorValidation Validation,
    [property: Id(5)] string[] Diagnostics,
    [property: Id(6)] DateTimeOffset CreatedAt,
    [property: Id(7)] BehaviorInputPolicy InputPolicy = BehaviorInputPolicy.EveryEvent,
    [property: Id(8)] string? RuntimeFingerprint = null,
    [property: Id(9)] string? SourceHash = null);

[GenerateSerializer, Alias("db.behavior-view")]
public sealed record BehaviorView(
    [property: Id(0)] NeuronId Id,
    [property: Id(1)] PrincipalId? Principal,
    [property: Id(2)] BehaviorProgram? Draft,
    [property: Id(3)] BehaviorProgram? Active,
    [property: Id(4)] bool Enabled,
    [property: Id(5)] long Epoch,
    [property: Id(6)] int PendingCount,
    [property: Id(7)] string? Detail);

[GenerateSerializer, Alias("db.behavior-save-script")]
public sealed record SaveBehaviorScript(
    [property: Id(0)] string Source,
    [property: Id(1)] string[]? InputSignalTypes = null,
    [property: Id(2)] string[]? OutputSignalTypes = null,
    [property: Id(3)] Guid? ExpectedDraftRevision = null,
    [property: Id(4)] BehaviorInputPolicy InputPolicy = BehaviorInputPolicy.EveryEvent) : Signal<BehaviorRead>, ICheckpointedRequest;

[GenerateSerializer, Alias("db.behavior-read")]
public sealed record ReadBehavior : Signal<BehaviorRead>;
[GenerateSerializer, Alias("db.behavior-read-result")]
public sealed record BehaviorRead([property: Id(0)] BehaviorView Behavior) : Signal;
[GenerateSerializer, Alias("db.behavior-enable")]
public sealed record EnableBehavior([property: Id(0)] Guid? ExpectedDraftRevision = null) : Signal<BehaviorRead>, ICheckpointedRequest;
[GenerateSerializer, Alias("db.behavior-disable")]
public sealed record DisableBehavior : Signal<BehaviorRead>, ICheckpointedRequest;
[GenerateSerializer, Alias("db.behavior-invoke")]
public sealed record InvokeBehavior([property: Id(0)] Signal Input) : Signal<BehaviorRead>, ICheckpointedRequest;
[GenerateSerializer, Alias("db.behavior-work-available")]
public sealed record BehaviorWorkAvailable([property: Id(0)] NeuronId Behavior) : Signal;
[GenerateSerializer, Alias("db.behavior-state-changed")]
public sealed record BehaviorStateChanged([property: Id(0)] NeuronId Behavior) : Signal;

[GenerateSerializer, Alias("db.behavior-claim")]
public sealed record BehaviorClaim(
    [property: Id(0)] Guid WorkId,
    [property: Id(1)] Guid Token,
    [property: Id(2)] long Epoch,
    [property: Id(3)] BehaviorProgram Program,
    [property: Id(4)] SignalDelivery Input,
    [property: Id(5)] PrincipalId? Principal,
    [property: Id(6)] int Attempt,
    [property: Id(7)] string? SourceStream = null,
    [property: Id(8)] long? StreamGeneration = null);

[GenerateSerializer, Alias("db.behavior-checkpoint")]
public sealed record BehaviorCheckpoint(
    [property: Id(0)] string Key,
    [property: Id(1)] string RequestHash,
    [property: Id(2)] Signal Response);
