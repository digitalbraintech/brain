using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Scripting.Applications;

public sealed partial class ApplicationAuthoringService : IApplicationAuthoring
{
    public Task<ApplicationInvocation> ReadInvocationAsync(IDigitalBrain brain, string key,
        Guid operationId, CancellationToken cancellationToken = default)
        => brain.Application(key).ReadInvocationAsync(operationId, cancellationToken);

    public Task<ApplicationInvocation> CancelInvocationAsync(IDigitalBrain brain, string key,
        Guid operationId, CancellationToken cancellationToken = default)
        => brain.Application(key).CancelInvocationAsync(operationId, cancellationToken);

    public Task<ApplicationInvocation> InvokeAsync(IDigitalBrain brain, string key, string operation,
        string inputJson, Guid operationId, CancellationToken cancellationToken = default)
        => brain.Application(key).InvokeDeclaredAsync(operation, inputJson, operationId, cancellationToken);

    private const int MaximumSourceBytes = 512 * 1024;
    private readonly string storeRoot;
    private readonly FileApplicationCompiler compiler;
    private readonly ApplicationArtifactHost host;
    private readonly ILogger<ApplicationAuthoringService> logger;
    private readonly IReadOnlyDictionary<string, IApplicationScenarioDriver> scenarioDrivers;
    private readonly IReadOnlyList<ApplicationNeuronCapabilityRegistration> neuronCapabilities;
    private readonly IReadOnlyList<ApplicationNeuronInputRegistration> neuronInputs;
    private readonly IReadOnlyList<ApplicationNeuronEventRegistration> neuronEvents;

    public ApplicationAuthoringService(
        string storeRoot,
        FileApplicationCompiler? compiler = null,
        ApplicationArtifactHost? host = null,
        ILogger<ApplicationAuthoringService>? logger = null,
        IEnumerable<IApplicationScenarioDriver>? scenarioDrivers = null,
        IEnumerable<ApplicationNeuronCapabilityRegistration>? neuronCapabilities = null,
        IEnumerable<ApplicationNeuronInputRegistration>? neuronInputs = null,
        IEnumerable<ApplicationNeuronEventRegistration>? neuronEvents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        this.storeRoot = Path.GetFullPath(storeRoot);
        this.compiler = compiler ?? new();
        this.host = host ?? new();
        this.logger = logger ?? NullLogger<ApplicationAuthoringService>.Instance;
        this.scenarioDrivers = (scenarioDrivers ?? [])
            .ToDictionary(driver => driver.Kind, StringComparer.Ordinal);
        this.neuronCapabilities = [.. neuronCapabilities ?? []];
        this.neuronInputs = [.. neuronInputs ?? []];
        this.neuronEvents = [.. neuronEvents ?? []];
    }

    public async Task<ApplicationSource> SaveAsync(
        IDigitalBrain brain,
        string key,
        string source,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        Directory.CreateDirectory(location.Directory);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false);
        if (metadata is null ? expectedRevision is not null :
            !StringComparer.Ordinal.Equals(metadata.SourceRevision, expectedRevision))
        {
            throw new InvalidOperationException("The application source changed. Read the current revision before saving.");
        }

        return await SaveFileCoreAsync(location, key, metadata?.EntryPath ?? "application.cs", source,
            metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApplicationSource> ReadAsync(
        IDigitalBrain brain,
        string key,
        CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        var metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Application '{key}' has no saved source.");
        var source = await File.ReadAllTextAsync(metadata.SourcePath, cancellationToken).ConfigureAwait(false);
        return new(
            key, metadata.SourceRevision, source,
            metadata.ActiveArtifact?.ApplicationRevision, metadata.PendingActivation?.ApplicationRevision,
            metadata.EntryPath ?? "application.cs");
    }

    internal async Task<ApplicationSource?> TryReadAsync(IDigitalBrain brain, string key,
        CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadataAsync(LocationFor(brain, key), cancellationToken).ConfigureAwait(false);
        if (metadata is null) { return null; }
        return new(key, metadata.SourceRevision,
            await File.ReadAllTextAsync(metadata.SourcePath, cancellationToken).ConfigureAwait(false),
            metadata.ActiveArtifact?.ApplicationRevision, metadata.PendingActivation?.ApplicationRevision,
            metadata.EntryPath ?? "application.cs");
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(IDigitalBrain brain, string key,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(LocationFor(brain, key), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Application '{key}' has no saved source.");
        IEnumerable<string> paths = metadata.Files is { } files
            ? files.Keys : [metadata.EntryPath ?? "application.cs"];
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<ApplicationSource> ReadFileAsync(IDigitalBrain brain, string key, string path,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBundlePath(path);
        var metadata = await ReadMetadataAsync(LocationFor(brain, key), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Application '{key}' has no saved source.");
        var sourcePath = metadata.Files?.GetValueOrDefault(normalized)
            ?? (normalized == (metadata.EntryPath ?? "application.cs") ? metadata.SourcePath : null)
            ?? throw new FileNotFoundException($"Application source file '{normalized}' does not exist.");
        return new(key, metadata.SourceRevision,
            await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false),
            metadata.ActiveArtifact?.ApplicationRevision, metadata.PendingActivation?.ApplicationRevision, normalized);
    }

    public async Task<ApplicationSource> SaveFileAsync(IDigitalBrain brain, string key, string path, string source,
        string expectedRevision, CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await RequireRevisionAsync(location, expectedRevision, cancellationToken).ConfigureAwait(false);
        return await SaveFileCoreAsync(location, key, NormalizeBundlePath(path), source, metadata, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApplicationSourceSummary>> ListAsync(
        IDigitalBrain brain,
        CancellationToken cancellationToken = default)
    {
        var tenantDirectory = TenantDirectory(brain);
        if (!Directory.Exists(tenantDirectory))
        {
            return [];
        }

        var results = new List<ApplicationSourceSummary>();
        foreach (var metadataPath in Directory.EnumerateFiles(
                     tenantDirectory, "metadata.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await ReadMetadataPathAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            if (metadata is not null)
            {
                results.Add(new(metadata.Key, metadata.SourceRevision,
                    metadata.Validation?.Succeeded == true, metadata.ActiveArtifact?.ApplicationRevision,
                    metadata.PendingActivation?.ApplicationRevision));
            }
        }

        return results.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
    }

    public async Task<ApplicationValidation> ValidateAsync(
        IDigitalBrain brain,
        string key,
        string expectedSourceRevision,
        CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await RequireRevisionAsync(location, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
        StoredValidation storedValidation;
        try
        {
            var inspected = await compiler.CompileInspectedAsync(metadata.SourcePath,
                Path.Combine(location.Directory, "artifacts"), brain, key, host, cancellationToken)
                .ConfigureAwait(false);
            storedValidation = new(expectedSourceRevision, true, null, inspected.Artifact,
                Describe(key, expectedSourceRevision, inspected));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            storedValidation = new(expectedSourceRevision, false, error.Message, null);
        }

        var artifacts = new Dictionary<string, FileApplicationArtifact>(metadata.Artifacts ?? [], StringComparer.Ordinal);
        if (storedValidation.Artifact is { } compiled)
        {
            artifacts[compiled.ApplicationRevision] = compiled;
        }
        await WriteMetadataAsync(
                location, metadata with { Validation = storedValidation, Artifacts = artifacts }, cancellationToken)
            .ConfigureAwait(false);
        return new(expectedSourceRevision, storedValidation.Succeeded, storedValidation.Diagnostics);
    }

    public async Task<ApplicationDescription> DescribeAsync(
        IDigitalBrain brain,
        string key,
        string expectedSourceRevision,
        CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await RequireRevisionAsync(location, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
        return metadata.Validation is
               { Succeeded: true, Description: { } description, SourceRevision: var validatedRevision }
               && StringComparer.Ordinal.Equals(validatedRevision, expectedSourceRevision)
            ? description
            : throw new InvalidOperationException(
                "The saved source revision must validate successfully before it can be described.");
    }

    private static ApplicationDescription Describe(
        string key,
        string sourceRevision,
        InspectedApplicationArtifact inspected)
    {
        var operations = inspected.Manifest.Operations
            .Where(static operation => operation.Kind == ApplicationOperationKind.Command &&
                                       operation.Key != "$apply" && operation.Trigger is null)
            .Select(static operation =>
            {
                var request = DescribeContract(operation.RequestContract);
                var response = DescribeContract(operation.ResponseContract);
                return new ApplicationOperationDescription(operation.Key,
                    $"{operation.Key}: {Label(request.Name)} → {Label(response.Name)}",
                    request, response, operation.IsQuery);
            })
            .ToArray();
        return new(key, sourceRevision, inspected.Artifact.ApplicationRevision, operations);
    }

    private static ApplicationJsonContract DescribeContract(string name) => name switch
    {
        "system.string/v1" => new(name, "{\"type\":\"string\"}", "\"hello\""),
        "system.boolean/v1" => new(name, "{\"type\":\"boolean\"}", "true"),
        "system.int32/v1" or "system.int64/v1" => new(name, "{\"type\":\"integer\"}", "1"),
        _ => new(name, null, null),
    };

    private static string Label(string name) => name switch
    {
        "system.string/v1" => "string",
        "system.boolean/v1" => "boolean",
        "system.int32/v1" or "system.int64/v1" => "integer",
        _ => name,
    };

    public async Task<ApplicationActivation> ActivateAsync(
        IDigitalBrain brain,
        string key,
        string expectedSourceRevision,
        CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await RequireRevisionAsync(location, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
        var artifact = metadata.Validation is { Succeeded: true, Artifact: { } validated } &&
                       metadata.Validation.SourceRevision == expectedSourceRevision
            ? validated
            : throw new InvalidOperationException("The saved source revision must validate successfully before activation.");
        if (metadata.ExpectationRevision is { } expectationRevision &&
            (metadata.ScenarioRun is not { Passed: true } authoritativeScenarios ||
             authoritativeScenarios.SourceRevision != expectedSourceRevision ||
             authoritativeScenarios.ArtifactRevision != artifact.ApplicationRevision ||
             authoritativeScenarios.ExpectationRevision != expectationRevision))
        {
            throw new InvalidOperationException("The recorded acceptance expectations must pass for this exact revision before activation.");
        }
        if (metadata.ExpectationRevision is null && metadata.Files?.ContainsKey("acceptance.json") == true &&
            (metadata.ScenarioRun is not { Passed: true } scenarios ||
             scenarios.SourceRevision != expectedSourceRevision || scenarios.ArtifactRevision != artifact.ApplicationRevision))
        {
            throw new InvalidOperationException("The saved acceptance examples must pass for this exact revision before activation.");
        }
        var pending = metadata with { PendingActivation = artifact };
        await WriteMetadataAsync(location, pending, cancellationToken).ConfigureAwait(false);
        await host.InstallAsync(artifact, brain, key, cancellationToken).ConfigureAwait(false);
        await WriteMetadataAsync(location, pending with { ActiveArtifact = artifact, PendingActivation = null }, cancellationToken)
            .ConfigureAwait(false);
        return new(key, expectedSourceRevision, artifact.ApplicationRevision);
    }

    public async Task ServeAsync(
        IDigitalBrain brain,
        string key,
        CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        var metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Application '{key}' has no saved source.");
        if (metadata.PendingActivation is not null)
        {
            await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
            metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Application '{key}' has no saved source.");
            if (metadata.PendingActivation is { } stillPending)
            {
                await host.InstallAsync(stillPending, brain, key, cancellationToken).ConfigureAwait(false);
                metadata = metadata with { ActiveArtifact = stillPending, PendingActivation = null };
                await WriteMetadataAsync(location, metadata, cancellationToken).ConfigureAwait(false);
            }
        }
        var artifact = metadata.ActiveArtifact
            ?? throw new InvalidOperationException($"Application '{key}' has no active artifact.");
        await host.ServeAsync(artifact, brain, key, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<bool> ImportFactoryBundleAsync(IDigitalBrain brain, string key, string entryPath,
        IReadOnlyDictionary<string, string> sources, CancellationToken cancellationToken)
    {
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        if (await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false) is not null) { return false; }
        var normalized = sources.ToDictionary(item => NormalizeBundlePath(item.Key), item => item.Value,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var entry = NormalizeBundlePath(entryPath);
        if (!normalized.ContainsKey(entry)) { throw new InvalidOperationException("The factory bundle entry file is missing."); }
        _ = await WriteBundleCoreAsync(location, key, entry, normalized, null, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<ApplicationSource> SaveFileCoreAsync(Location location, string key, string path,
        string source, AuthoringMetadata? metadata, CancellationToken cancellationToken)
    {
        ValidateSource(source);
        var sources = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (metadata?.Files is { } files)
        {
            foreach (var file in files)
            {
                sources[file.Key] = await File.ReadAllTextAsync(file.Value, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (metadata is not null)
        {
            sources[metadata.EntryPath ?? "application.cs"] = await File.ReadAllTextAsync(
                metadata.SourcePath, cancellationToken).ConfigureAwait(false);
        }
        sources[path] = source;
        var saved = await WriteBundleCoreAsync(location, key, metadata?.EntryPath ?? path, sources, metadata,
            cancellationToken).ConfigureAwait(false);
        return saved with { Source = source, Path = path };
    }

    private static async Task<ApplicationSource> WriteBundleCoreAsync(Location location, string key,
        string entryPath, IReadOnlyDictionary<string, string> sources, AuthoringMetadata? metadata,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources.Values) { ValidateSource(source); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in sources.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var pathBytes = Encoding.UTF8.GetBytes(file.Key);
            var sourceBytes = Encoding.UTF8.GetBytes(file.Value);
            hash.AppendData(BitConverter.GetBytes(pathBytes.Length));
            hash.AppendData(pathBytes);
            hash.AppendData(BitConverter.GetBytes(sourceBytes.Length));
            hash.AppendData(sourceBytes);
        }
        var revision = Convert.ToHexStringLower(hash.GetHashAndReset());
        var directory = Path.Combine(location.Directory, "sources", revision);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in sources)
        {
            var destination = Path.Combine(directory, file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                await WriteAtomicAsync(destination, Encoding.UTF8.GetBytes(file.Value), cancellationToken)
                    .ConfigureAwait(false);
            }
            files[file.Key] = destination;
        }
        var sourcePath = files[entryPath];
        var retainedValidation = metadata?.SourceRevision == revision ? metadata.Validation : null;
        var next = new AuthoringMetadata(location.Owner, location.Principal, key, revision, sourcePath,
            retainedValidation, metadata?.ActiveArtifact, metadata?.PendingActivation,
            metadata?.Artifacts ?? [], entryPath, files,
            metadata?.SourceRevision == revision ? metadata.ScenarioRun : null,
            metadata?.ExpectationRevision, metadata?.Expectations, metadata?.ExpectationOperations);
        await WriteMetadataAsync(location, next, cancellationToken).ConfigureAwait(false);
        return new(key, revision, sources[entryPath], next.ActiveArtifact?.ApplicationRevision,
            next.PendingActivation?.ApplicationRevision, entryPath);
    }

    private static void ValidateSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bytes = Encoding.UTF8.GetByteCount(source);
        if (string.IsNullOrWhiteSpace(source)) { throw new ArgumentException("Application source cannot be empty."); }
        if (bytes > MaximumSourceBytes) { throw new ArgumentException("Application source cannot exceed 512 KiB of UTF-8 content."); }
    }

    private static string NormalizeBundlePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').Trim('/');
        if (Path.IsPathRooted(path) || normalized.Length == 0
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Application file paths must stay inside the source bundle.", nameof(path));
        }
        return normalized.Equals("acceptance.json", StringComparison.OrdinalIgnoreCase) ? "acceptance.json" : normalized;
    }

    private Location LocationFor(IDigitalBrain brain, string key)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Contains('/') || key.Contains('\\'))
        {
            throw new ArgumentException("Application keys cannot contain path separators.", nameof(key));
        }

        var tenant = TenantDirectory(brain);
        var applicationPart = PartitionHash(key);
        var actor = VerifiedActor.Current!;
        return new(
            Path.Combine(tenant, applicationPart),
            Path.Combine(tenant, applicationPart, "metadata.json"),
            brain.Owner.Value,
            actor.PrincipalId.Value);
    }

    private string TenantDirectory(IDigitalBrain brain)
    {
        var actor = VerifiedActor.Current
            ?? throw new InvalidOperationException("Application authoring requires an authenticated principal.");
        var connectionActor = brain switch
        {
            DigitalBrainClient client => client.ConnectionActor,
            BorrowedDigitalBrain borrowed when borrowed.Inner is DigitalBrainClient client => client.ConnectionActor,
            _ => null,
        };
        if (connectionActor is not null && connectionActor.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException("This application connection belongs to another principal.");
        }
        return Path.Combine(storeRoot, PartitionHash($"{brain.Owner.Value}\0{actor.PrincipalId}"));
    }

    private static async Task<AuthoringMetadata> RequireRevisionAsync(
        Location location,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        var metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The application has no saved source.");
        return StringComparer.Ordinal.Equals(metadata.SourceRevision, expectedRevision)
            ? metadata
            : throw new InvalidOperationException("The application source changed. Read the current revision and retry.");
    }

    private static async Task<AuthoringMetadata?> ReadMetadataAsync(
        Location location,
        CancellationToken cancellationToken)
        => await ReadMetadataPathAsync(location.MetadataPath, cancellationToken).ConfigureAwait(false);

    private static async Task<AuthoringMetadata?> ReadMetadataPathAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var stream = new FileStream(
            metadataPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<AuthoringMetadata>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task WriteMetadataAsync(
        Location location,
        AuthoringMetadata metadata,
        CancellationToken cancellationToken)
        => WriteAtomicAsync(
            location.MetadataPath, JsonSerializer.SerializeToUtf8Bytes(metadata), cancellationToken);

    private static async Task WriteAtomicAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task<FileStream> AcquireLockAsync(Location location, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(location.Directory);
        var lockPath = Path.Combine(location.Directory, ".write.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string PartitionHash(string value) => Hash(value)[..24];

    internal async Task<IReadOnlyList<SupervisorRegistration>> ReadSupervisorRegistrationsAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(storeRoot))
        {
            return [];
        }
        var registrations = new List<SupervisorRegistration>();
        foreach (var metadataPath in Directory.EnumerateFiles(storeRoot, "metadata.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var metadata = await ReadMetadataPathAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                if (metadata is null)
                {
                    continue;
                }

                var expectedPath = MetadataPathFor(metadata.Owner, metadata.Principal, metadata.Key);
                if (!StringComparer.OrdinalIgnoreCase.Equals(
                        Path.GetFullPath(metadataPath), Path.GetFullPath(expectedPath)))
                {
                    throw new InvalidDataException("Application metadata does not match its storage partition.");
                }

                if (metadata.ActiveArtifact is not null || metadata.PendingActivation is not null)
                {
                    registrations.Add(new(
                        metadata.Owner, metadata.Principal, metadata.Key,
                        metadata.ActiveArtifact, metadata.PendingActivation,
                        metadata.Artifacts ?? []));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogError(error, "Ignoring invalid application authoring metadata at {MetadataPath}.", metadataPath);
            }
        }
        return registrations;
    }

    private string MetadataPathFor(string owner, Guid principal, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (principal == Guid.Empty)
        {
            throw new ArgumentException("Application metadata must identify a principal.", nameof(principal));
        }
        if (key.Contains('/') || key.Contains('\\'))
        {
            throw new ArgumentException("Application keys cannot contain path separators.", nameof(key));
        }

        var tenant = Path.Combine(storeRoot, PartitionHash($"{owner}\0{principal:n}"));
        return Path.Combine(tenant, PartitionHash(key), "metadata.json");
    }

    internal async Task CompletePendingActivationAsync(
        IDigitalBrain brain,
        SupervisorRegistration registration,
        CancellationToken cancellationToken)
    {
        if (registration.PendingActivation is null)
        {
            return;
        }
        var location = LocationFor(brain, registration.Key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The supervised application metadata disappeared.");
        if (metadata.PendingActivation is { } pending)
        {
            await host.InstallAsync(pending, brain, registration.Key, cancellationToken).ConfigureAwait(false);
            await WriteMetadataAsync(
                location, metadata with { ActiveArtifact = pending, PendingActivation = null }, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed record Location(string Directory, string MetadataPath, string Owner, Guid Principal);
    private sealed record AuthoringMetadata(
        string Owner,
        Guid Principal,
        string Key,
        string SourceRevision,
        string SourcePath,
        StoredValidation? Validation,
        FileApplicationArtifact? ActiveArtifact,
        FileApplicationArtifact? PendingActivation,
        Dictionary<string, FileApplicationArtifact>? Artifacts,
        string? EntryPath = null,
        Dictionary<string, string>? Files = null,
        ApplicationScenarioRun? ScenarioRun = null,
        string? ExpectationRevision = null,
        Dictionary<string, StoredExpectation>? Expectations = null,
        Dictionary<Guid, string>? ExpectationOperations = null);
    private sealed record StoredExpectation(
        string ExpectationRevision,
        string DocumentPath,
        Guid OperationId,
        Guid PrincipalId,
        DateTimeOffset RecordedAt);
    private sealed record StoredValidation(
        string SourceRevision,
        bool Succeeded,
        string? Diagnostics,
        FileApplicationArtifact? Artifact,
        ApplicationDescription? Description = null);
}

internal sealed record SupervisorRegistration(
    string Owner,
    Guid Principal,
    string Key,
    FileApplicationArtifact? ActiveArtifact,
    FileApplicationArtifact? PendingActivation,
    IReadOnlyDictionary<string, FileApplicationArtifact> Artifacts);
