namespace DigitalBrain.Abstractions.Scripting;

public sealed record ApplicationScenarioRun(string SourceRevision, string ArtifactRevision,
    bool Passed, IReadOnlyList<ApplicationScenarioResult> Examples, string? ExpectationRevision = null);

public sealed record ApplicationScenarioResult(string Name, bool Passed, string? ActualJson, string? Error);
