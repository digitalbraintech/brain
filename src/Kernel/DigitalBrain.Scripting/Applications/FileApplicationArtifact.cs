namespace DigitalBrain.Scripting.Applications;

public sealed record FileApplicationArtifact(
    string SourcePath,
    string ArtifactPath,
    string SourceHash,
    string RevisionId,
    IReadOnlyDictionary<string, FileApplicationArtifact>? Children = null,
    string? EffectiveRevisionId = null)
{
    public string ApplicationRevision => EffectiveRevisionId ?? RevisionId;
}
