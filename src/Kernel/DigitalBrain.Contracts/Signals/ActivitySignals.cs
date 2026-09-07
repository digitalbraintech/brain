using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

/// <summary>Observation traffic and all its descendants stay outside activity accounting.</summary>
public interface IActivityTelemetry;

[GenerateSerializer, Alias("db.activity-execution-changed")]
public sealed record ActivityExecutionChanged(
    [property: Id(0)] CorrelationId CorrelationId,
    [property: Id(1)] PrincipalId? Principal,
    [property: Id(2)] string OperationId,
    [property: Id(3)] SignalId SignalId,
    [property: Id(4)] SignalId? CausationId,
    [property: Id(5)] NeuronId Source,
    [property: Id(6)] NeuronId? Target,
    [property: Id(7)] string SignalType,
    [property: Id(8)] string Phase,
    [property: Id(9)] DateTimeOffset Timestamp,
    [property: Id(10)] string? Title = null,
    [property: Id(11)] string? CommandId = null,
    [property: Id(12)] string? Detail = null) : Signal, IActivityTelemetry;

[GenerateSerializer, Alias("db.read-activities")]
public sealed record ReadActivities([property: Id(0)] int Limit = 100) : Signal<ActivitiesSnapshot>, IActivityTelemetry;

[GenerateSerializer, Alias("db.activities-snapshot")]
public sealed record ActivitiesSnapshot(
    [property: Id(0)] DateTimeOffset ObservedAt,
    [property: Id(1)] ActivityView[] Activities) : Signal, IActivityTelemetry;

[GenerateSerializer, Alias("db.activity-changed")]
public sealed record ActivityChanged([property: Id(0)] ActivityView Activity) : Signal, IActivityTelemetry;

[GenerateSerializer, Alias("db.activity-view")]
public sealed record ActivityView(
    [property: Id(0)] string Id,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] string RootSignalId,
    [property: Id(3)] string TriggerName,
    [property: Id(4)] string Title,
    [property: Id(5)] string Status,
    [property: Id(6)] DateTimeOffset StartedAt,
    [property: Id(7)] DateTimeOffset UpdatedAt,
    [property: Id(8)] string[] ParticipantNeuronIds,
    [property: Id(9)] ActivityEventView[] Events,
    [property: Id(10)] string? CommandId = null,
    [property: Id(11)] string? Detail = null,
    [property: Id(12)] PrincipalId? Principal = null,
    [property: Id(13)] long Version = 0);

[GenerateSerializer, Alias("db.activity-event-view")]
public sealed record ActivityEventView(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string SignalId,
    [property: Id(2)] string? CausationId,
    [property: Id(3)] string SourceNeuronId,
    [property: Id(4)] string? TargetNeuronId,
    [property: Id(5)] string SignalType,
    [property: Id(6)] string Phase,
    [property: Id(7)] DateTimeOffset Timestamp,
    [property: Id(9)] string? Detail = null);
