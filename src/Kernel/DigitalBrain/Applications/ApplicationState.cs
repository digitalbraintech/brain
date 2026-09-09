using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Core;

[GenerateSerializer, Alias("db.application-state")]
internal sealed record ApplicationState
{
    [Id(1)] public Dictionary<string, ApplicationManifest> Revisions { get; init; } = [];
    [Id(2)] public Dictionary<Guid, ApplicationWork> Work { get; init; } = [];
    [Id(3)] public Dictionary<string, ApplicationSharedState> Scopes { get; init; } = [];
    [Id(4)] public long NextAdmission { get; init; }
}

[GenerateSerializer, Alias("db.application-work")]
internal sealed record ApplicationWork(
    [property: Id(0)] Guid Id,
    [property: Id(1)] string Revision,
    [property: Id(2)] string Operation,
    [property: Id(3)] string Payload,
    [property: Id(4)] ApplicationResult Result,
    [property: Id(5)] Guid Lease = default,
    [property: Id(6)] DateTimeOffset LeaseUntil = default,
    [property: Id(7)] int Attempts = 0)
{
    [Id(8)] public List<ApplicationEffect> Effects { get; init; } = [];
    [Id(9)] public string? StateScope { get; init; }
    [Id(10)] public long AdmissionIndex { get; init; }
    [Id(11)] public Dictionary<string, string>? InitialState { get; init; }
    [Id(12)] public long ExpectedStateVersion { get; init; }
    [Id(13)] public Dictionary<string, ApplicationEffect> BranchEffects { get; init; } = [];
    [Id(14)] public int? WaitingEffectOrdinal { get; init; }
    [Id(15)] public ApplicationParent? Parent { get; init; }
}

[GenerateSerializer, Alias("db.application-shared-state")]
internal sealed record ApplicationSharedState(
    [property: Id(0)] string Schema,
    [property: Id(1)] long Version,
    [property: Id(2)] Dictionary<string, string> Values);
