using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Applications;

public sealed class FileApplicationCompiler
{
    private const string FileBuildTargets = """
        <Project>
          <PropertyGroup Condition="'$(FileBasedProgram)' == 'true'">
            <_GlobalPropertiesToRemoveFromProjectReferences>$(_GlobalPropertiesToRemoveFromProjectReferences);CustomAfterMicrosoftCommonTargets</_GlobalPropertiesToRemoveFromProjectReferences>
            <!-- Match Silo/MCP: file apps are not library code; CA2007 is noise under AnalysisLevel preview. -->
            <NoWarn>$(NoWarn);CA2007</NoWarn>
          </PropertyGroup>
          <Target Name="DigitalBrainRemoveFileApplicationOrleansGenerator" BeforeTargets="CoreCompile"
                  Condition="'$(FileBasedProgram)' == 'true'">
            <ItemGroup>
              <Analyzer Remove="@(Analyzer)" Condition="'%(Analyzer.Filename)' == 'Orleans.CodeGenerator'" />
            </ItemGroup>
          </Target>
        </Project>
        """;

    public Task<FileApplicationArtifact> CompileAsync(
        string sourcePath,
        string outputRoot,
        IDigitalBrain brain,
        string applicationKey,
        ApplicationArtifactHost? host = null,
        CancellationToken cancellationToken = default)
        => new ApplicationArtifactGraphCompiler(this, host ?? new ApplicationArtifactHost())
            .CompileAsync(sourcePath, applicationKey, outputRoot, brain, cancellationToken);

    internal Task<InspectedApplicationArtifact> CompileInspectedAsync(
        string sourcePath,
        string outputRoot,
        IDigitalBrain brain,
        string applicationKey,
        ApplicationArtifactHost host,
        CancellationToken cancellationToken = default)
        => new ApplicationArtifactGraphCompiler(this, host)
            .CompileInspectedAsync(sourcePath, applicationKey, outputRoot, brain, cancellationToken);

    public async Task<FileApplicationArtifact> CompileAsync(
        string sourcePath,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The file-based application source does not exist.", fullSourcePath);
        }

        var fullOutputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(fullOutputRoot);

        var stagingPath = Path.Combine(fullOutputRoot, $".staging-{Guid.NewGuid():N}");
        var publishPath = Path.Combine(stagingPath, "publish");
        var snapshotDirectory = Path.Combine(stagingPath, "source");
        Directory.CreateDirectory(publishPath);
        Directory.CreateDirectory(snapshotDirectory);

        try
        {
            var sourceBytes = await File.ReadAllBytesAsync(fullSourcePath, cancellationToken);
            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
            var snapshotPath = Path.Combine(snapshotDirectory, Path.GetFileName(fullSourcePath));
            await File.WriteAllBytesAsync(snapshotPath, sourceBytes, cancellationToken);
            var buildTargetsPath = Path.Combine(stagingPath, "file-application.targets");
            await File.WriteAllTextAsync(
                buildTargetsPath, FileBuildTargets, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

            await PublishAsync(fullSourcePath, publishPath, buildTargetsPath, cancellationToken);
            var sourceAfterPublish = await File.ReadAllBytesAsync(fullSourcePath, cancellationToken);
            if (!sourceBytes.AsSpan().SequenceEqual(sourceAfterPublish))
            {
                throw new InvalidOperationException($"The source file '{fullSourcePath}' changed during publish.");
            }

            var artifactFileName = $"{Path.GetFileNameWithoutExtension(fullSourcePath)}.dll";
            var stagedArtifactPath = Path.Combine(publishPath, artifactFileName);
            if (!File.Exists(stagedArtifactPath))
            {
                throw new InvalidOperationException($"The SDK publish completed without producing '{artifactFileName}'.");
            }

            var revisionId = await ComputeRevisionIdAsync(stagingPath, cancellationToken);
            var revisionPath = Path.Combine(fullOutputRoot, revisionId);
            try
            {
                Directory.Move(stagingPath, revisionPath);
            }
            catch (IOException) when (Directory.Exists(revisionPath))
            {
                var existingRevisionId = await ComputeRevisionIdAsync(revisionPath, cancellationToken);
                var existingSourcePath = Path.Combine(revisionPath, "source", Path.GetFileName(fullSourcePath));
                var existingArtifactPath = Path.Combine(revisionPath, "publish", artifactFileName);
                if (!StringComparer.Ordinal.Equals(existingRevisionId, revisionId) ||
                    !File.Exists(existingSourcePath) ||
                    !File.Exists(existingArtifactPath))
                {
                    throw new IOException($"Artifact revision path '{revisionPath}' already exists with different or incomplete content.");
                }

                Directory.Delete(stagingPath, recursive: true);
            }

            return new(
                Path.Combine(revisionPath, "source", Path.GetFileName(fullSourcePath)),
                Path.Combine(revisionPath, "publish", artifactFileName),
                sourceHash,
                revisionId);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            throw;
        }
    }

    private static async Task PublishAsync(
        string sourcePath,
        string publishPath,
        string buildTargetsPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(sourcePath)!,
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(publishPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(
            typeof(FileApplicationCompiler).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? "Release");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add($"-p:CustomAfterMicrosoftCommonTargets={buildTargetsPath}");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the .NET SDK publisher.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The .NET SDK could not publish '{sourcePath}'.{Environment.NewLine}{output}{error}");
        }
    }

    internal static async Task<string> ComputeRevisionIdAsync(string directory, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
            var nameBytes = Encoding.UTF8.GetBytes(relativePath);
            hash.AppendData(BitConverter.GetBytes(nameBytes.Length));
            hash.AppendData(nameBytes);

            await using var stream = File.OpenRead(path);
            hash.AppendData(BitConverter.GetBytes(stream.Length));
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
