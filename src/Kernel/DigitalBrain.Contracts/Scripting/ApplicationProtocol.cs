using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Scripting;

[GenerateSerializer, Alias("db.application-publication")]
public sealed record ApplicationPublication([property: Id(0)] Guid EventId, [property: Id(1)] int RecipientCount);

// Runtime protocol, not the author-facing grain contract.
[Alias("db.application-kernel")]
internal interface IApplicationKernel : IGrainWithStringKey
{
    Task<Guid> SubmitDeclared(Guid operationId, string operation, string payload);
    Task<ApplicationInvocation> ReadInvocation(Guid operationId);
    Task Install(ApplicationManifest manifest, bool activate = true);
    Task<Guid[]> Activate(string revision);
    Task<Guid> Submit(Guid operationId, string operation, string requestContract, string responseContract, string payload);
    Task<Guid> SubmitPinned(Guid operationId, string revision, string operation, string requestContract, string responseContract, string payload, ApplicationParent? parent = null);
    [Orleans.Concurrency.AlwaysInterleave]
    Task<bool> CancellationRequested(Guid operationId, int remainingDepth);
    Task<ApplicationClaim?> Claim(string revision);
    Task Complete(Guid operationId, Guid lease, string result, int effectCount, int branchEffectCount,
        Dictionary<string, string> writes);
    Task Fail(Guid operationId, Guid lease, string error);
    Task<bool> Renew(Guid operationId, Guid lease);
    Task<ApplicationResult> Read(Guid operationId);
    Task Cancel(Guid operationId);
    Task<ApplicationResult?> ReadIfKnown(Guid operationId);
    Task<string> Head();
    Task<string[]> PendingRevisions();
    Task<bool> Declares(string revision, string operation);
    Task<ApplicationEffect> BeginEffect(Guid operationId, Guid lease, int ordinal, string identity, string? revision, Dictionary<string, string> writes);
    Task<bool> BeginDelay(Guid operationId, Guid lease, int ordinal, string identity, long durationTicks,
        Dictionary<string, string> writes);
    Task ArmEventWait(Guid operationId, Guid lease, int ordinal, string identity,
        string sourceKind, string sourceId, string behaviorKey, string outputKey, string contract,
        Dictionary<string, string> writes);
    Task<string?> AwaitEventWait(Guid operationId, Guid lease, int ordinal, string identity);
    Task FinishEffect(Guid operationId, Guid lease, int ordinal, string result);
    Task<ApplicationEffect> BeginBranchEffect(Guid operationId, Guid lease, string branch, string identity,
        string? revision);
    Task FinishBranchEffect(Guid operationId, Guid lease, string branch, string result);
}

[GenerateSerializer, Alias("db.application-parent")]
internal sealed record ApplicationParent(
    [property: Id(0)] string ApplicationIdentity,
    [property: Id(1)] Guid OperationId);

[Alias("db.application-call-source")]
internal interface IApplicationCallSource : IGrainWithStringKey
{
    Task<SignalDelivery> Prepare(Signal request, SignalId signalId, CorrelationId correlation, long sourceEpoch);
}

[GenerateSerializer, Alias("db.application-effect")]
internal sealed record ApplicationEffect(
    [property: Id(0)] string Identity,
    [property: Id(1)] Guid OperationId,
    [property: Id(2)] string? Revision,
    [property: Id(3)] string? Result = null)
{
    [Id(4)] public Dictionary<string, string> Writes { get; init; } = [];
    [Id(5)] public DateTimeOffset? DueAt { get; init; }
    [Id(6)] public string? WaitSourceId { get; init; }
    [Id(7)] public string? WaitContract { get; init; }
    [Id(8)] public long? WaitAfterSequence { get; init; }
    [Id(9)] public string? WaitSourceKind { get; init; }
    [Id(10)] public string? WaitBehaviorKey { get; init; }
    [Id(11)] public string? WaitOutputKey { get; init; }
}

[GenerateSerializer, Alias("db.application-manifest")]
internal sealed record ApplicationManifest(
    [property: Id(0)] string Revision,
    [property: Id(1)] ApplicationOperation[] Operations,
    [property: Id(2)] ApplicationEventOutput[]? Outputs = null,
    [property: Id(3)] ApplicationEventConnection[]? Connections = null);

[GenerateSerializer, Alias("db.application-event-output")]
internal sealed record ApplicationEventOutput(
    [property: Id(0)] string SourceKind, [property: Id(1)] string SourceId,
    [property: Id(2)] string BehaviorKey, [property: Id(3)] string Key,
    [property: Id(4)] string Contract);

[GenerateSerializer, Alias("db.application-event-connection")]
internal sealed record ApplicationEventConnection(
    [property: Id(0)] string Key, [property: Id(1)] ApplicationEventOutput Source,
    [property: Id(2)] string TargetApplicationKey, [property: Id(3)] string TargetOperation,
    [property: Id(4)] string Contract,
    [property: Id(5)] string? TargetNeuronId = null);

[GenerateSerializer, Alias("db.application-operation")]
internal sealed record ApplicationOperation(
    [property: Id(0)] string Key,
    [property: Id(1)] string RequestContract,
    [property: Id(2)] string ResponseContract,
    [property: Id(3)] ApplicationTextTrigger? Trigger = null,
    [property: Id(4)] string? StateScope = null,
    [property: Id(5)] string? StateSchema = null,
    [property: Id(6)] string? ImplementedContract = null,
    [property: Id(7)] string? ImplementedInstance = null,
    [property: Id(8)] bool IsQuery = false,
    [property: Id(9)] ApplicationOperationKind Kind = ApplicationOperationKind.Command);

internal enum ApplicationOperationKind
{
    Command,
    Input,
}

[GenerateSerializer, Alias("db.application-text-trigger")]
internal sealed record ApplicationTextTrigger(
    [property: Id(0)] string SourceId,
    [property: Id(1)] string Contract,
    [property: Id(2)] string Text,
    [property: Id(3)] bool ContainsIgnoreCase = false);

[Alias("db.application-catalog")]
internal interface IApplicationCatalog : IGrainWithStringKey
{
    Task<Guid[]> Activate(string applicationKey, ApplicationManifest manifest);
    Task<string> Head(string applicationKey);
    Task<bool> HasEventRoute(string sourceId, string contract);
    Task<ApplicationRoute?> Admit(Guid signalId, Guid correlationId, string sourceId, string contract, string text, string payload);
    Task<ApplicationRoute?> AdmitContract(Guid signalId, Guid correlationId, string implementedContract,
        string implementedInstance, string requestContract, string responseContract, string payload);
    Task<ApplicationPublication> Publish(ApplicationEventEmission emission);
    Task<ApplicationEventDelivery?> ClaimDelivery(string applicationKey, string revision);
    Task AckDelivery(Guid deliveryId, Guid lease);
    Task<string[]> PendingDeliveryRevisions(string applicationKey);
    Task<long> EventCursor(ApplicationEventOutput source);
    Task<string?> ReadEventAfter(ApplicationEventOutput source, long afterSequence);
}

[GenerateSerializer, Alias("db.application-event-emission")]
internal sealed record ApplicationEventEmission(
    [property: Id(0)] Guid EventId, [property: Id(1)] Guid CorrelationId,
    [property: Id(2)] string SourceKind, [property: Id(3)] string SourceId,
    [property: Id(4)] string SourceRevision, [property: Id(5)] string BehaviorKey,
    [property: Id(6)] string OutputKey, [property: Id(7)] string Contract,
    [property: Id(8)] string Payload);

[GenerateSerializer, Alias("db.application-event-delivery")]
internal sealed record ApplicationEventDelivery(
    [property: Id(0)] Guid DeliveryId, [property: Id(1)] Guid OperationId,
    [property: Id(2)] Guid Lease, [property: Id(3)] string ApplicationKey,
    [property: Id(4)] string Revision, [property: Id(5)] string Operation,
    [property: Id(6)] string Contract, [property: Id(7)] string Payload,
    [property: Id(8)] string? TargetNeuronId = null,
    [property: Id(9)] SignalDelivery? ObservedDelivery = null);

[GenerateSerializer, Alias("db.application-route")]
internal sealed record ApplicationRoute(
    [property: Id(0)] string ApplicationKey,
    [property: Id(1)] string Revision,
    [property: Id(2)] ApplicationOperation Operation);

[GenerateSerializer, Alias("db.application-claim")]
internal sealed record ApplicationClaim(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] Guid Lease,
    [property: Id(2)] string Revision,
    [property: Id(3)] string Operation,
    [property: Id(4)] string Payload,
    [property: Id(5)] string? StateScope = null,
    [property: Id(6)] string? StateSchema = null,
    [property: Id(7)] Dictionary<string, string>? InitialState = null);

[GenerateSerializer, Alias("db.application-result")]
internal sealed record ApplicationResult(
    [property: Id(0)] string Status,
    [property: Id(1)] string? Value = null,
    [property: Id(2)] string? Error = null);
