using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Applications;

internal sealed class ApplicationArtifactGraphCompiler(
    FileApplicationCompiler compiler,
    ApplicationArtifactHost host)
{
    internal async Task<FileApplicationArtifact> CompileAsync(
        string sourcePath, string applicationKey, string outputRoot, IDigitalBrain brain,
        CancellationToken cancellationToken)
        => (await CompileInspectedAsync(sourcePath, applicationKey, outputRoot, brain, cancellationToken)
            .ConfigureAwait(false)).Artifact;

    internal Task<InspectedApplicationArtifact> CompileInspectedAsync(
        string sourcePath, string applicationKey, string outputRoot, IDigitalBrain brain,
        CancellationToken cancellationToken)
        => CompileAsync(Path.GetFullPath(sourcePath), applicationKey, outputRoot, brain,
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            new(StringComparer.Ordinal), cancellationToken);

    private async Task<InspectedApplicationArtifact> CompileAsync(
        string sourcePath, string applicationKey, string outputRoot, IDigitalBrain brain,
        HashSet<string> stack, Dictionary<string, string> keys, CancellationToken cancellationToken)
    {
        if (!stack.Add(sourcePath)) { throw new InvalidOperationException($"Child script cycle reaches '{sourcePath}'."); }
        if (keys.TryGetValue(applicationKey, out var existing))
        {
            throw new InvalidOperationException(
                $"Script key '{applicationKey}' is already assigned to '{existing}'.");
        }
        keys[applicationKey] = sourcePath;
        try
        {
            var artifact = await compiler.CompileAsync(sourcePath, outputRoot, cancellationToken).ConfigureAwait(false);
            var inspection = await host.InspectAsync(artifact, brain, applicationKey, cancellationToken).ConfigureAwait(false);
            var declarations = inspection.Scripts;
            var duplicate = declarations.GroupBy(item => item.Key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null) { throw new InvalidOperationException($"Script key '{duplicate.Key}' is declared twice."); }
            var children = new Dictionary<string, FileApplicationArtifact>(StringComparer.Ordinal);
            foreach (var declaration in declarations.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var childPath = Path.GetFullPath(declaration.RelativePath, Path.GetDirectoryName(sourcePath)!);
                if (!File.Exists(childPath))
                {
                    throw new FileNotFoundException($"Child script '{declaration.Key}' does not exist.", childPath);
                }
                children[declaration.Key] = (await CompileAsync(childPath, declaration.Key, outputRoot, brain,
                    stack, keys, cancellationToken).ConfigureAwait(false)).Artifact;
            }
            if (children.Count == 0) { return new(artifact, inspection.Manifest); }
            var graph = artifact with { Children = children };
            return new(graph with { EffectiveRevisionId = ApplicationArtifactGraphRevision.Compute(graph) }, inspection.Manifest);
        }
        finally
        {
            stack.Remove(sourcePath);
        }
    }
}

internal sealed record InspectedApplicationArtifact(
    FileApplicationArtifact Artifact,
    DigitalBrain.Abstractions.Scripting.ApplicationManifest Manifest);
