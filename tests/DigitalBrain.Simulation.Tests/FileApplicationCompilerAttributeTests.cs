using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DigitalBrain.Scripting.Applications;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class FileApplicationCompilerAttributeTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        FindRepositoryRoot(), ".file-compiler-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Published_file_application_does_not_advertise_an_Orleans_application_part()
    {
        Directory.CreateDirectory(testRoot);
        var sdkProject = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Sdk", "DigitalBrain.Sdk.csproj")
            .Replace('\\', '/');
        var sourcePath = Path.Combine(testRoot, "application.cs");
        await File.WriteAllTextAsync(sourcePath, $$"""
            #:project {{sdkProject}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            brain.Application("metadata").Command<string, string>("reply", (value, _, _) => Task.FromResult(value));
            await brain.Application("metadata").RunAsync(args);
            """, TestContext.Current.CancellationToken);

        var artifact = await new FileApplicationCompiler().CompileAsync(
            sourcePath, Path.Combine(testRoot, "artifacts"), TestContext.Current.CancellationToken);

        using var stream = File.OpenRead(artifact.ArtifactPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var attributes = metadata.GetAssemblyDefinition().GetCustomAttributes()
            .Select(handle => AttributeType(metadata, metadata.GetCustomAttribute(handle)))
            .ToArray();

        Assert.DoesNotContain("Orleans.ApplicationPartAttribute", attributes);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot)) { Directory.Delete(testRoot, recursive: true); }
    }

    private static string AttributeType(MetadataReader metadata, CustomAttribute attribute)
    {
        EntityHandle declaringType = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };
        return declaringType.Kind switch
        {
            HandleKind.TypeReference => Name(metadata, metadata.GetTypeReference((TypeReferenceHandle)declaringType)),
            HandleKind.TypeDefinition => Name(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)declaringType)),
            _ => string.Empty,
        };
    }

    private static string Name(MetadataReader metadata, TypeReference type)
        => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";

    private static string Name(MetadataReader metadata, TypeDefinition type)
        => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
