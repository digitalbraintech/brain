using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Scripting.Applications;

public sealed class ShippedApplicationInstaller(ApplicationAuthoringService authoring)
{
    public async Task<ApplicationActivation> EnsureActiveAsync(IDigitalBrain brain, string key,
        string entryPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var current = await authoring.TryReadAsync(brain, key, cancellationToken).ConfigureAwait(false);
        if (current?.ActiveRevision is { } retained)
        {
            return new(key, current.SourceRevision, retained);
        }
        if (current is not null)
        {
            return await ValidateAndActivateAsync(brain, key, current, cancellationToken).ConfigureAwait(false);
        }
        var fullEntry = Path.GetFullPath(entryPath);
        var directory = Path.GetDirectoryName(fullEntry)!;
        var sources = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                path => NormalizeProjects(File.ReadAllText(path), Path.GetDirectoryName(path)!), StringComparer.Ordinal);
        _ = await authoring.ImportFactoryBundleAsync(brain, key, Path.GetFileName(fullEntry), sources,
            cancellationToken).ConfigureAwait(false);
        current = await authoring.ReadAsync(brain, key, cancellationToken).ConfigureAwait(false);
        return await ValidateAndActivateAsync(brain, key, current, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApplicationActivation> ValidateAndActivateAsync(IDigitalBrain brain, string key,
        ApplicationSource current, CancellationToken cancellationToken)
    {
        var validation = await authoring.ValidateAsync(brain, key, current.SourceRevision, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException($"The shipped application failed validation: {validation.Diagnostics}");
        }
        return await authoring.ActivateAsync(brain, key, current.SourceRevision, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeProjects(string source, string sourceDirectory)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            const string prefix = "#:project ";
            if (!lines[index].StartsWith(prefix, StringComparison.Ordinal)) { continue; }
            var value = lines[index][prefix.Length..].Trim().Trim('"');
            if (Path.IsPathRooted(value)) { continue; }
            var resolved = Path.GetFullPath(value, sourceDirectory);
            if (!File.Exists(resolved) || !resolved.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Factory application project reference '{value}' is unavailable.");
            }
            lines[index] = $"{prefix}{resolved.Replace('\\', '/')}";
        }
        return string.Join('\n', lines);
    }
}
