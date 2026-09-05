using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

[GenerateSerializer]
internal sealed record BehaviorState
{
    [Id(0)] public PrincipalId? Principal { get; set; }
    [Id(1)] public BehaviorProgram? Draft { get; set; }
    [Id(2)] public BehaviorProgram? Active { get; set; }
    [Id(3)] public bool Enabled { get; set; }
    [Id(4)] public bool ActivateRequested { get; set; }
    [Id(5)] public long Epoch { get; set; }
    [Id(6)] public string? Detail { get; set; }
    [Id(7)] public List<BehaviorWork> Work { get; set; } = [];
    [Id(8)] public HashSet<string> Accepted { get; set; } = [];
    [Id(9)] public List<BehaviorOutput> Outbox { get; set; } = [];
    [Id(10)] public List<BehaviorSubscription> Subscriptions { get; set; } = [];
    [Id(11)] public bool SubscriptionsPending { get; set; }
    [Id(12)] public HashSet<NeuronId> OutputTargets { get; set; } = [];
    [Id(13)] public HashSet<NeuronId> PendingFences { get; set; } = [];
    [Id(14)] public DateTimeOffset EnabledAt { get; set; }
    [Id(15)] public Dictionary<string, BehaviorSubject> Subjects { get; set; } = [];
    [Id(16)] public List<BehaviorCancellation> Cancellations { get; set; } = [];
    [Id(17)] public Dictionary<SignalId, BehaviorCommandResult> Commands { get; set; } = [];
    [Id(18)] public long SubscriptionVersion { get; set; }
    [Id(19)] public List<BehaviorSubscriptionIntent> SubscriptionIntents { get; set; } = [];
}

[GenerateSerializer]
internal sealed record BehaviorCommandResult([property: Id(0)] string Hash, [property: Id(1)] BehaviorView View);

[GenerateSerializer]
internal sealed record BehaviorWork
{
    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public long Epoch { get; init; }
    [Id(2)] public SignalDelivery Input { get; init; } = null!;
    [Id(3)] public Guid? ClaimToken { get; set; }
    [Id(4)] public DateTimeOffset LeaseUntil { get; set; }
    [Id(5)] public int Attempts { get; set; }
    [Id(6)] public Dictionary<string, BehaviorCheckpoint> Checkpoints { get; set; } = [];
    [Id(7)] public bool Terminal { get; set; }
    [Id(8)] public string? Detail { get; set; }
    [Id(9)] public DateTimeOffset RetryAfter { get; set; }
    [Id(10)] public Dictionary<string, BehaviorPreparedRequest> Requests { get; set; } = [];
    [Id(11)] public string? SubjectKey { get; init; }
    [Id(12)] public long? SubjectGeneration { get; init; }
    [Id(13)] public string? CompletionKey { get; init; }
    [Id(14)] public BehaviorProgram Program { get; init; } = null!;
}

[GenerateSerializer]
internal sealed record BehaviorCancellation([property: Id(0)] Guid WorkId,
    [property: Id(1)] Guid Revision, [property: Id(2)] string Reason);

[GenerateSerializer]
internal sealed record BehaviorPreparedRequest([property: Id(0)] string Hash,
    [property: Id(1)] NeuronId Receiver, [property: Id(2)] SignalDelivery Delivery,
    [property: Id(3)] bool Acknowledged = false);

[GenerateSerializer]
internal sealed record BehaviorOutput
{
    [Id(0)] public long Epoch { get; init; }
    [Id(1)] public SignalDelivery Delivery { get; init; } = null!;
    [Id(2)] public NeuronId[] Recipients { get; init; } = [];
    [Id(3)] public HashSet<NeuronId> Acknowledged { get; set; } = [];
    [Id(4)] public bool Recorded { get; set; }
    [Id(5)] public string? SubjectKey { get; init; }
    [Id(6)] public long? SubjectGeneration { get; init; }
    [Id(7)] public string? CompletionKey { get; init; }
    [Id(8)] public SignalDelivery? Input { get; init; }
}

[GenerateSerializer]
internal sealed record BehaviorSubject
{
    [Id(0)] public string Version { get; set; } = "";
    [Id(1)] public long Generation { get; set; }
    [Id(2)] public HashSet<string> Completed { get; set; } = [];
    [Id(3)] public HashSet<NeuronId> Targets { get; set; } = [];
    [Id(4)] public bool FencePending { get; set; }
    [Id(5)] public DateTimeOffset ObservedAt { get; set; }
    [Id(6)] public Dictionary<string, Signal> FrozenOutputs { get; set; } = [];
    [Id(7)] public BehaviorInputAuthority? Authority { get; set; }
    [Id(8)] public long? FencedSourceEpoch { get; set; }
    [Id(9)] public long? FencedSourceGeneration { get; set; }
}

[GenerateSerializer]
internal sealed record BehaviorInputAuthority([property: Id(0)] NeuronId Source,
    [property: Id(1)] long? Epoch, [property: Id(2)] string? Stream,
    [property: Id(3)] long? Generation)
{
    public static BehaviorInputAuthority From(SignalDelivery input) => new(input.Caller, input.SourceEpoch,
        input.SourceStream, input.StreamGeneration);
}

[GenerateSerializer]
internal sealed record BehaviorSubscription([property: Id(0)] NeuronId Source, [property: Id(1)] string SignalType);

[GenerateSerializer]
internal sealed record BehaviorSubscriptionIntent([property: Id(0)] BehaviorSubscription Subscription,
    [property: Id(1)] bool Subscribed, [property: Id(2)] long Version);
