using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Signals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.Scripting.Startup;

internal sealed record StartupScript(string Path, string Source, string Sha256)
{
    public ScriptBehavior? Behavior { get; init; }
    public Signal? Input { get; init; }
    public SourceReferenceResolver? SourceResolver { get; init; }

    public static StartupScript FromSource(string path, string source)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        return new StartupScript(path, source, ComputeSha256(sourceBytes));
    }

    public static async Task<StartupScript> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var sources = new Dictionary<string, byte[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        await ReadSource(fullPath);
        var sourceBytes = sources[fullPath];
        var hash = ComputeSha256(sourceBytes);
        if (sources.Count > 1)
        {
            using var bundleHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (sourcePath, bytes) in sources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var relativePath = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(fullPath)!, sourcePath);
                bundleHash.AppendData(Encoding.UTF8.GetBytes(relativePath + "\0" + bytes.Length + "\0"));
                bundleHash.AppendData(bytes);
            }
            hash = Convert.ToHexStringLower(bundleHash.GetHashAndReset());
        }
        return new StartupScript(fullPath, Encoding.UTF8.GetString(sourceBytes), hash)
        {
            SourceResolver = new StartupSourceResolver(sources, fullPath),
        };

        async Task ReadSource(string sourcePath)
        {
            if (sources.ContainsKey(sourcePath))
            {
                return;
            }
            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
            sources.Add(sourcePath, bytes);
            var syntax = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes),
                new CSharpParseOptions(kind: SourceCodeKind.Script), sourcePath, cancellationToken: cancellationToken);
            foreach (var load in syntax.GetRoot(cancellationToken).DescendantTrivia(descendIntoTrivia: true)
                .Select(trivia => trivia.GetStructure()).OfType<LoadDirectiveTriviaSyntax>().Where(load => load.IsActive))
            {
                var dependency = System.IO.Path.GetFullPath(load.File.ValueText, System.IO.Path.GetDirectoryName(sourcePath)!);
                await ReadSource(dependency);
            }
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> sourceBytes)
        => Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
}

// Resolve #load from the bytes admitted into the execution ledger, even if a file changes
// between compilation and execution. Saved behavior programs retain their separate runner.
internal sealed class StartupSourceResolver(IReadOnlyDictionary<string, byte[]> sources, string entryPath) : SourceReferenceResolver
{
    public override string? NormalizePath(string path, string? baseFilePath)
        => System.IO.Path.GetFullPath(path, System.IO.Path.GetDirectoryName(baseFilePath ?? entryPath)!);

    public override string? ResolveReference(string path, string? baseFilePath)
    {
        var resolved = NormalizePath(path, baseFilePath)!;
        return sources.ContainsKey(resolved) ? resolved : null;
    }

    public override Stream OpenRead(string resolvedPath) => new MemoryStream(sources[resolvedPath], writable: false);
    public override bool Equals(object? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
