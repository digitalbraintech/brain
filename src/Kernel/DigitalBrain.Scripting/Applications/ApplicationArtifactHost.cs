using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Chat;
using DigitalBrain.Core;

namespace DigitalBrain.Scripting.Applications;

public sealed partial class ApplicationArtifactHost
{
    private readonly ApplicationWorkerBootstrapServer? bootstrap;
    private readonly ApplicationWorkerCapabilityAuthority workerCapabilities;

    public ApplicationArtifactHost() => workerCapabilities = ApplicationWorkerCapabilityAuthority.Process;

    internal ApplicationArtifactHost(ApplicationWorkerBootstrapServer bootstrap)
    {
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        workerCapabilities = bootstrap.Capabilities;
    }

    public Task ValidateAsync(FileApplicationArtifact artifact, IDigitalBrain brain, string applicationKey,
        CancellationToken cancellationToken = default)
        => RunAsync(artifact, brain, applicationKey, ApplicationRunMode.Validate, cancellationToken);

    internal async Task<ApplicationArtifactInspection> InspectAsync(
        FileApplicationArtifact artifact, IDigitalBrain brain, string applicationKey,
        CancellationToken cancellationToken = default)
    {
        var scripts = new List<ApplicationScriptReference>();
        var manifests = new List<ApplicationManifest>();
        await RunAsync(artifact, brain, applicationKey, ApplicationRunMode.Validate, cancellationToken, scripts,
                manifests: manifests)
            .ConfigureAwait(false);
        return new(scripts, manifests.Single());
    }

    private static readonly Assembly[] SharedBoundaryAssemblies =
    [
        typeof(IDigitalBrain).Assembly,
        typeof(DigitalBrainClient).Assembly,
        typeof(UserMessaged).Assembly,
    ];

    public async Task InstallAsync(
        FileApplicationArtifact artifact,
        IDigitalBrain brain,
        string applicationKey,
        CancellationToken cancellationToken = default)
    {
        var actor = ActorFor(brain, applicationKey);
        using var actorScope = VerifiedActor.Enter(actor);
        await StageChildrenAsync(artifact, brain, cancellationToken).ConfigureAwait(false);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workers = StartChildWorkers(artifact, brain, stopping.Token);
        var capability = workerCapabilities.Issue(brain.Owner.Value, actor.PrincipalId.Value, applicationKey,
            artifact.ApplicationRevision, DateTimeOffset.MaxValue);
        Task? installing = null;
        Exception? primaryFailure = null;
        try
        {
            using var scope = ApplicationWorkerCapabilityContext.Enter(capability);
            installing = RunAsync(artifact, brain, applicationKey, ApplicationRunMode.Install, stopping.Token,
                childRevisions: ChildRevisions(artifact));
            if (workers.Count != 0)
            {
                var firstWorker = Task.WhenAny(workers).Unwrap();
                var completed = await Task.WhenAny(installing, firstWorker).ConfigureAwait(false);
                if (completed == firstWorker)
                {
                    await firstWorker.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Child workers stopped before parent installation completed.");
                }
            }
            await installing.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            try
            {
                await DrainInstallationAsync(installing, workers, primaryFailure is not null, stopping.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                workerCapabilities.Revoke(capability);
            }
        }
    }

    private async Task StageChildrenAsync(
        FileApplicationArtifact artifact, IDigitalBrain brain, CancellationToken cancellationToken)
    {
        if (artifact.Children is not { } children) { return; }
        foreach (var child in children)
        {
            await StageChildrenAsync(child.Value, brain, cancellationToken).ConfigureAwait(false);
            await RunAsync(child.Value, brain, child.Key, ApplicationRunMode.Stage, cancellationToken,
                childRevisions: ChildRevisions(child.Value)).ConfigureAwait(false);
        }
    }

    private List<Task> StartChildWorkers(
        FileApplicationArtifact artifact, IDigitalBrain brain, CancellationToken cancellationToken)
    {
        var workers = new List<Task>();
        if (artifact.Children is not { } children) { return workers; }
        foreach (var child in children)
        {
            workers.AddRange(StartChildWorkers(child.Value, brain, cancellationToken));
            workers.Add(ServeSingleAsync(child.Value, brain, child.Key, cancellationToken));
        }
        return workers;
    }

    private static IReadOnlyDictionary<string, string>? ChildRevisions(FileApplicationArtifact artifact)
        => artifact.Children?.ToDictionary(
            item => item.Key, item => item.Value.ApplicationRevision, StringComparer.Ordinal);

    public Task ServeAsync(
        FileApplicationArtifact artifact,
        IDigitalBrain brain,
        string applicationKey,
        CancellationToken cancellationToken = default)
        => ServeGraphAsync(artifact, brain, applicationKey, cancellationToken);

    private async Task ServeGraphAsync(FileApplicationArtifact artifact, IDigitalBrain brain,
        string applicationKey, CancellationToken cancellationToken)
    {
        var actor = ActorFor(brain, applicationKey);
        using var actorScope = VerifiedActor.Enter(actor);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workers = StartChildWorkers(artifact, brain, stopping.Token);
        workers.Add(ServeSingleAsync(artifact, brain, applicationKey, stopping.Token));
        try
        {
            var completed = await Task.WhenAny(workers).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("An application graph worker stopped before the graph was cancelled.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Cancellation is the normal end of a supervised worker graph.
        }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            await DrainWorkersAsync(workers).ConfigureAwait(false);
        }
    }

    private static async Task DrainWorkersAsync(IEnumerable<Task> workers)
    {
        foreach (var worker in workers)
        {
            try { await worker.ConfigureAwait(false); }
            catch (Exception) { }
        }
    }

    private static async Task DrainInstallationAsync(
        Task? installing,
        IEnumerable<Task> workers,
        bool preservePrimaryFailure,
        CancellationToken stoppingToken)
    {
        ExceptionDispatchInfo? cleanupFailure = null;
        foreach (var task in installing is null ? workers : workers.Prepend(installing))
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                cleanupFailure ??= ExceptionDispatchInfo.Capture(error);
            }
        }
        if (!preservePrimaryFailure) { cleanupFailure?.Throw(); }
    }

    private Task ServeSingleAsync(FileApplicationArtifact artifact, IDigitalBrain brain,
        string applicationKey, CancellationToken cancellationToken)
        => bootstrap is null
            ? RunTrustedAsync(artifact, brain, applicationKey, cancellationToken)
            : RunProcessAsync(artifact, brain, applicationKey, cancellationToken);

    private async Task RunTrustedAsync(FileApplicationArtifact artifact, IDigitalBrain brain,
        string applicationKey, CancellationToken cancellationToken)
    {
        var actor = ActorFor(brain, applicationKey);
        using var actorScope = VerifiedActor.Enter(actor);
        var capability = workerCapabilities.Issue(brain.Owner.Value, actor.PrincipalId.Value, applicationKey,
            artifact.ApplicationRevision, DateTimeOffset.MaxValue);
        try
        {
            using var scope = ApplicationWorkerCapabilityContext.Enter(capability);
            await RunAsync(artifact, brain, applicationKey, ApplicationRunMode.Serve, cancellationToken,
                    childRevisions: ChildRevisions(artifact))
                .ConfigureAwait(false);
        }
        finally
        {
            workerCapabilities.Revoke(capability);
        }
    }

    private async Task RunProcessAsync(
        FileApplicationArtifact artifact,
        IDigitalBrain brain,
        string applicationKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationKey);
        await VerifyGraphAsync(artifact, cancellationToken).ConfigureAwait(false);
        var actor = ActorFor(brain, applicationKey);
        using var actorScope = VerifiedActor.Enter(actor);
        var ticket = bootstrap!.Issue(artifact, brain.Owner, actor, applicationKey);
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(artifact.ArtifactPath))!,
            },
            EnableRaisingEvents = true,
        };
        process.StartInfo.ArgumentList.Add(Path.GetFullPath(artifact.ArtifactPath));
        process.StartInfo.ArgumentList.Add("--digitalbrain-worker-endpoint");
        process.StartInfo.ArgumentList.Add(bootstrap.Endpoint.AbsoluteUri);
        process.StartInfo.ArgumentList.Add("--digitalbrain-worker-ticket");
        process.StartInfo.ArgumentList.Add(ticket);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The application worker process could not be started.");
            }
            var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var cancellation = cancellationToken.Register(static state =>
            {
                var child = (Process)state!;
                try
                {
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }, process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Application worker exited with code {process.ExitCode}: {error.Trim()}");
            }
        }
        finally
        {
            bootstrap.Revoke(ticket);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task RunAsync(
        FileApplicationArtifact artifact,
        IDigitalBrain brain,
        string applicationKey,
        ApplicationRunMode mode,
        CancellationToken cancellationToken,
        List<ApplicationScriptReference>? scripts = null,
        IReadOnlyDictionary<string, string>? childRevisions = null,
        List<ApplicationManifest>? manifests = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationKey);
        var boundaries = BoundaryAssemblies(artifact.ArtifactPath);
        await VerifyGraphAsync(artifact, cancellationToken).ConfigureAwait(false);

        var loadContext = new ArtifactLoadContext(artifact.ArtifactPath, boundaries);
        try
        {
            await RunLoadedArtifactAsync(
                loadContext, artifact, brain, applicationKey, mode, cancellationToken, scripts, childRevisions, manifests).ConfigureAwait(false);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static async Task RunLoadedArtifactAsync(
        ArtifactLoadContext loadContext,
        FileApplicationArtifact artifact,
        IDigitalBrain brain,
        string applicationKey,
        ApplicationRunMode mode,
        CancellationToken cancellationToken,
        List<ApplicationScriptReference>? scripts,
        IReadOnlyDictionary<string, string>? childRevisions,
        List<ApplicationManifest>? manifests)
    {
        await using var artifactStream = File.OpenRead(Path.GetFullPath(artifact.ArtifactPath));
        var assembly = loadContext.LoadFromStream(artifactStream);
        var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException(
            $"Artifact '{artifact.ArtifactPath}' has no executable entry point.");
        using var brainScope = DigitalBrainClient.BindExecution(brain);
        using var runScope = ApplicationRunScope.Enter(
            mode, artifact.ApplicationRevision, applicationKey, cancellationToken, scripts, childRevisions, manifests);
        await InvokeEntryPointAsync(entryPoint).ConfigureAwait(false);
        ApplicationRunScope.RequireCompletedDefinition();
    }

    private static async Task VerifyAsync(
        FileApplicationArtifact artifact,
        IReadOnlyList<Assembly> boundaries,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(artifact.SourcePath);
        var artifactPath = Path.GetFullPath(artifact.ArtifactPath);
        var sourceDirectory = Directory.GetParent(sourcePath);
        var publishDirectory = Directory.GetParent(artifactPath);
        var sourceRevision = sourceDirectory?.Parent;
        var artifactRevision = publishDirectory?.Parent;
        if (sourceDirectory?.Name != "source" || publishDirectory?.Name != "publish" ||
            sourceRevision is null || artifactRevision is null ||
            !PathsEqual(sourceRevision.FullName, artifactRevision.FullName) ||
            !StringComparer.Ordinal.Equals(sourceRevision.Name, artifact.RevisionId) ||
            !File.Exists(sourcePath) || !File.Exists(artifactPath))
        {
            throw new InvalidOperationException("Artifact verification failed: paths do not identify one complete compiler revision.");
        }

        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
        var revisionId = await FileApplicationCompiler.ComputeRevisionIdAsync(
            sourceRevision.FullName, cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(sourceHash, artifact.SourceHash) ||
            !StringComparer.Ordinal.Equals(revisionId, artifact.RevisionId))
        {
            throw new InvalidOperationException("Artifact verification failed: retained content does not match its recorded hashes.");
        }

        foreach (var shared in boundaries)
        {
            var publishedPath = Path.Combine(publishDirectory.FullName, $"{shared.GetName().Name}.dll");
            if (!File.Exists(publishedPath) || string.IsNullOrEmpty(shared.Location))
            {
                throw new InvalidOperationException(
                    $"Artifact verification failed: shared boundary assembly '{shared.GetName().Name}' does not match the host.");
            }

            var publishedBytes = await File.ReadAllBytesAsync(publishedPath, cancellationToken).ConfigureAwait(false);
            var sharedBytes = await File.ReadAllBytesAsync(shared.Location, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(publishedBytes), SHA256.HashData(sharedBytes)))
            {
                throw new InvalidOperationException(
                    $"Artifact verification failed: shared boundary assembly '{shared.GetName().Name}' does not match the host.");
            }
        }
    }

    private static async Task VerifyGraphAsync(
        FileApplicationArtifact artifact, CancellationToken cancellationToken)
    {
        await VerifyAsync(artifact, BoundaryAssemblies(artifact.ArtifactPath), cancellationToken).ConfigureAwait(false);
        foreach (var child in artifact.Children ?? new Dictionary<string, FileApplicationArtifact>())
        {
            await VerifyGraphAsync(child.Value, cancellationToken).ConfigureAwait(false);
        }
        if (artifact.Children is { Count: > 0 })
        {
            var expected = ApplicationArtifactGraphRevision.Compute(artifact);
            if (!StringComparer.Ordinal.Equals(expected, artifact.EffectiveRevisionId))
            {
                throw new InvalidOperationException("Artifact verification failed: child graph revision does not match its mapping.");
            }
        }
        else if (artifact.EffectiveRevisionId is not null)
        {
            throw new InvalidOperationException("Artifact verification failed: an empty child graph cannot have an effective revision.");
        }
    }

    private static IReadOnlyList<Assembly> BoundaryAssemblies(string artifactPath)
    {
        var publishDirectory = Path.GetDirectoryName(Path.GetFullPath(artifactPath))!;
        var optionalContracts = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name is { } name
                && name.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal)
                && name.EndsWith(".Contracts", StringComparison.Ordinal)
                && File.Exists(Path.Combine(publishDirectory, $"{name}.dll")));
        return SharedBoundaryAssemblies.Concat(optionalContracts)
            .DistinctBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task InvokeEntryPointAsync(MethodInfo entryPoint)
    {
        object? invocation;
        try
        {
            invocation = entryPoint.GetParameters().Length switch
            {
                0 => entryPoint.Invoke(null, null),
                1 => entryPoint.Invoke(null, [Array.Empty<string>()]),
                _ => throw new InvalidOperationException("Artifact entry point has an unsupported signature."),
            };
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }

        if (invocation is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static ActorContext ActorFor(IDigitalBrain brain, string applicationKey)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationKey);
        var actor = brain.Application(applicationKey).Actor;
        if (VerifiedActor.Current is { } current && current.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException(
                "The application host cannot use a brain connection belonging to another principal.");
        }
        return actor;
    }

    private sealed class ArtifactLoadContext(string artifactPath, IEnumerable<Assembly> sharedAssemblies)
        : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(artifactPath);
        private readonly IReadOnlyDictionary<string, Assembly> shared = sharedAssemblies.ToDictionary(
            assembly => assembly.GetName().Name!, StringComparer.Ordinal);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && shared.TryGetValue(assemblyName.Name, out var boundary))
            {
                return AssemblyName.ReferenceMatchesDefinition(boundary.GetName(), assemblyName)
                    ? boundary
                    : throw new InvalidOperationException(
                        $"Artifact boundary assembly '{assemblyName}' does not match the host.");
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null) { return null; }
            using var assembly = File.OpenRead(path);
            var symbolsPath = Path.ChangeExtension(path, ".pdb");
            if (!File.Exists(symbolsPath)) { return LoadFromStream(assembly); }
            using var symbols = File.OpenRead(symbolsPath);
            return LoadFromStream(assembly, symbols);
        }
    }
}

internal sealed record ApplicationArtifactInspection(
    IReadOnlyList<ApplicationScriptReference> Scripts,
    ApplicationManifest Manifest);
