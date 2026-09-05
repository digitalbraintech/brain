namespace DigitalBrain.Kernel;

// HTTP projections, not graph state. Every snapshot is rebuilt from the neuron-owned
// synapses and bounded journals; identifiers are relative to the authenticated owner.
internal sealed record BrainGraphSnapshot(
    string RootId,
    DateTimeOffset ObservedAt,
    bool Truncated,
    string Scope,
    IReadOnlyList<BrainGraphNode> Nodes,
    IReadOnlyList<BrainGraphSynapse> Synapses,
    IReadOnlyList<BrainGraphActivity> Activity,
    IReadOnlyList<BrainCorrelation> Correlations);

internal sealed record BrainGraphNode(
    string Id,
    string Type,
    string Name,
    string Label,
    string Module,
    string Role,
    string Status,
    IReadOnlyList<string> HandledSignals,
    long IncomingSequence,
    long OutgoingSequence,
    DateTimeOffset? LastActivityAt,
    string? IconKey = null,
    IReadOnlyList<string>? OutputSignals = null,
    Guid? ActiveRevision = null,
    Guid? DraftRevision = null,
    bool IsInfrastructure = false,
    int InputPolicy = 0);

internal sealed record BrainGraphSynapse(
    string Id,
    string SourceId,
    string TargetId,
    string SignalType,
    string Kind,
    double Weight,
    long FireCount,
    DateTimeOffset LastFiredAt,
    bool IsBlocking,
    bool CanUnsubscribe);

internal sealed record BrainGraphActivity(
    string Id,
    string NeuronId,
    string Direction,
    long Sequence,
    string SignalType,
    DateTimeOffset? Timestamp,
    string CallerId,
    string CorrelationId,
    string Summary,
    IReadOnlyDictionary<string, string>? PayloadPreview,
    Guid? OperationId = null,
    string? Kind = null,
    string? State = null,
    string? Name = null,
    string? TargetId = null,
    string? Server = null,
    double? DurationMs = null,
    string? ResultPreview = null,
    bool IsError = false,
    bool Truncated = false,
    string? FailureCode = null);

internal sealed record BrainCorrelation(
    string CorrelationId,
    string Status,
    string Summary,
    DateTimeOffset? LastAt,
    IReadOnlyList<string> NeuronIds,
    int DeliveryCount);

internal sealed record BrainGraphSubscriptionRequest(
    string SourceId,
    string TargetId,
    string SignalType,
    bool Subscribed);

internal sealed record BrainGraphSubscriptionResult(
    string SourceId,
    string TargetId,
    string SignalType,
    bool Subscribed);
