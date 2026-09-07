namespace DigitalBrain.Abstractions.Scripting;

internal enum ApplicationRunMode
{
    Validate,
    Stage,
    Install,
    Serve,
}

internal static class ApplicationRunScope
{
    private static readonly AsyncLocal<Context?> Current = new();
    private static Context? processContext;

    internal static IDisposable EnterProcess(
        string revision, string applicationKey, string workerCapability,
        IReadOnlyDictionary<string, string>? childRevisions, CancellationToken cancellationToken)
    {
        var context = new Context(ApplicationRunMode.Serve, revision, applicationKey, cancellationToken,
            ChildRevisions: childRevisions, WorkerCapability: workerCapability);
        if (Interlocked.CompareExchange(ref processContext, context, null) is not null)
        {
            throw new InvalidOperationException("This process already hosts an application worker.");
        }
        return new ProcessScope(context);
    }

    internal static IDisposable Enter(
        ApplicationRunMode mode,
        string revision,
        string applicationKey,
        CancellationToken cancellationToken,
        List<ApplicationScriptReference>? scripts = null,
        IReadOnlyDictionary<string, string>? childRevisions = null,
        List<ApplicationManifest>? manifests = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationKey);
        var previous = Current.Value;
        var inherited = previous ?? Volatile.Read(ref processContext);
        Current.Value = new(mode, revision, applicationKey, cancellationToken,
            Scripts: scripts ?? inherited?.Scripts,
            ChildRevisions: childRevisions ?? inherited?.ChildRevisions,
            WorkerCapability: inherited?.WorkerCapability,
            Manifests: manifests ?? inherited?.Manifests);
        return new Scope(previous);
    }

    internal static async Task RunAsync(
        ApplicationDefinition application,
        string[] args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(args);
        var context = Current.Value ?? Volatile.Read(ref processContext) ?? throw new InvalidOperationException(
            "Application execution requires a verified artifact host scope.");
        if (Interlocked.Increment(ref context.RunBoundaries) != 1)
        {
            throw new InvalidOperationException("An application artifact must call RunAsync exactly once.");
        }
        var declaredKey = application.KernelKey;
        if (!StringComparer.Ordinal.Equals(declaredKey, context.ApplicationKey))
        {
            throw new InvalidOperationException(
                $"Artifact application identity '{declaredKey}' does not match expected key '{context.ApplicationKey}'.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken, cancellationToken);
        if (context.Mode == ApplicationRunMode.Validate)
        {
            context.Manifests?.Add(application.BuildManifest(context.Revision));
            return;
        }
        if (context.Mode == ApplicationRunMode.Stage)
        {
            await application.InstallAsync(context.Revision, linked.Token, activate: false).ConfigureAwait(false);
            return;
        }
        if (context.Mode == ApplicationRunMode.Install)
        {
            await application.ApplyAsync(context.Revision, linked.Token).ConfigureAwait(false);
            return;
        }

        await application.ServeAsync(context.Revision, linked.Token).ConfigureAwait(false);
    }

    internal static IDisposable EnterExecution(bool readOnly = false)
    {
        var context = Current.Value ?? Volatile.Read(ref processContext) ?? throw new InvalidOperationException(
            "Application handler execution requires a verified artifact host scope.");
        var previous = context;
        var permit = new ExecutionPermit();
        Current.Value = new(
            context.Mode,
            context.Revision,
            context.ApplicationKey,
            context.CancellationToken,
            readOnly ? ApplicationRunPhase.Query : ApplicationRunPhase.Execution,
            permit,
            context.Scripts,
            context.ChildRevisions,
            context.WorkerCapability);
        return new Scope(previous, permit);
    }

    internal static void ThrowIfDefinitionEffect(string operation)
    {
        var context = Current.Value ?? Volatile.Read(ref processContext);
        if (context?.Phase == ApplicationRunPhase.Query)
        {
            throw new InvalidOperationException($"A query cannot perform business effect '{operation}'.");
        }
        if (context is not null &&
            (context.Phase == ApplicationRunPhase.Definition || context.Permit?.IsActive != true))
        {
            throw new InvalidOperationException(
                $"Application definition cannot perform business effect '{operation}'. Declare the application and call RunAsync.");
        }
    }

    internal static void RecordScript(ApplicationScriptReference reference)
        => (Current.Value ?? Volatile.Read(ref processContext))?.Scripts?.Add(reference);

    internal static void RequireCompletedDefinition()
    {
        var context = Current.Value ?? Volatile.Read(ref processContext) ?? throw new InvalidOperationException(
            "Application validation requires a verified artifact host scope.");
        if (Volatile.Read(ref context.RunBoundaries) != 1)
        {
            throw new InvalidOperationException("An application artifact must call RunAsync exactly once.");
        }
    }

    internal static string ChildRevision(ApplicationScriptReference reference)
        => (Current.Value ?? Volatile.Read(ref processContext))?.ChildRevisions?
            .TryGetValue(reference.Key, out var revision) == true
            ? revision
            : throw new InvalidOperationException($"Child script '{reference.Key}' is not pinned by this artifact revision.");

    internal static string RequireWorkerCapability()
        => (Current.Value ?? Volatile.Read(ref processContext))?.WorkerCapability
            ?? ApplicationWorkerCapabilityContext.Current
            ?? throw new InvalidOperationException("Application execution requires a server-issued worker capability.");

    private sealed class Context(
        ApplicationRunMode mode,
        string revision,
        string applicationKey,
        CancellationToken cancellationToken,
        ApplicationRunPhase phase = ApplicationRunPhase.Definition,
        ExecutionPermit? permit = null,
        List<ApplicationScriptReference>? Scripts = null,
        IReadOnlyDictionary<string, string>? ChildRevisions = null,
        string? WorkerCapability = null,
        List<ApplicationManifest>? Manifests = null)
    {
        internal ApplicationRunMode Mode { get; } = mode;
        internal string Revision { get; } = revision;
        internal string ApplicationKey { get; } = applicationKey;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal ApplicationRunPhase Phase { get; } = phase;
        internal ExecutionPermit? Permit { get; } = permit;
        internal List<ApplicationScriptReference>? Scripts { get; } = Scripts;
        internal IReadOnlyDictionary<string, string>? ChildRevisions { get; } = ChildRevisions;
        internal string? WorkerCapability { get; } = WorkerCapability;
        internal List<ApplicationManifest>? Manifests { get; } = Manifests;
        internal int RunBoundaries;
    }

    private enum ApplicationRunPhase
    {
        Definition,
        Execution,
        Query,
    }

    private sealed class ExecutionPermit
    {
        private int active = 1;
        internal bool IsActive => Volatile.Read(ref active) == 1;
        internal void Deactivate() => Interlocked.Exchange(ref active, 0);
    }

    private sealed class Scope(Context? previous, ExecutionPermit? permit = null) : IDisposable
    {
        public void Dispose()
        {
            permit?.Deactivate();
            Current.Value = previous;
        }
    }

    private sealed class ProcessScope(Context context) : IDisposable
    {
        public void Dispose() => Interlocked.CompareExchange(ref processContext, null, context);
    }
}
