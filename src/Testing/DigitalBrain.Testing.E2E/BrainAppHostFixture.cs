using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Xunit;

namespace DigitalBrain.Testing.E2E;

public class BrainAppHostFixture<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    private const int DiagnosticLogLineCount = 40;

    private ResourceLogCollector? _logCollector;
    private IHost? _scriptHost;
    private IGrainFactory? _grains;

    private readonly BrainE2EOptions? _options;

    public BrainAppHostFixture()
    {
    }

    // Options-first construction for tests that build the fixture themselves; the parameterless
    // ctor exists so xunit can instantiate collection-fixture subclasses with default options.
    public BrainAppHostFixture(BrainE2EOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var options = _options ?? new();
        var appBuilder = await DistributedApplicationTestingBuilder
            .CreateAsync<TAppHost>(options.Args)
            .ConfigureAwait(false);

        // Aspire 13.5 advertises a test host through the same CLI discovery socket
        // as its real AppHost project, despite disabling the test dashboard. A
        // concurrent `aspire start` can attach to this fixture, report success with
        // no dashboard, and cancel its own unfinished launch. Give discovery the
        // actual test assembly identity; resource/project paths are already built.
        appBuilder.Configuration["AppHost:FilePath"] = typeof(TAppHost).Assembly.Location;

        // Keep test output readable: only warnings and errors reach the console. The apphost
        // re-logs every resource's console line under DigitalBrain.AppHost.Resources.* at
        // Information, and configuration rules outrank SetMinimumLevel, so the quieting must
        // go through configuration. Failure diagnostics are unaffected —
        // DescribeUnhealthyResources reads resource logs from ResourceLoggerService, which
        // streams regardless of console log level. Set DIGITALBRAIN_TEST_VERBOSE=1 to get the
        // full resource-log narrative back when diagnosing a run.
        if (Environment.GetEnvironmentVariable("DIGITALBRAIN_TEST_VERBOSE") is not "1")
        {
            appBuilder.Configuration["Logging:LogLevel:Default"] = nameof(LogLevel.Warning);
        }

        StubParameters(appBuilder);
        IsolateContainers(appBuilder);
        RandomizePorts(appBuilder);
        ArmExplicitStart(appBuilder, options.ExplicitStart);
        StripNeverStartingWaits(appBuilder, options.ExplicitStart);
        ArmProjectResources(appBuilder, options.ProjectEnvironment);

        App = await appBuilder.BuildAsync().ConfigureAwait(false);

        try
        {
            _logCollector = new ResourceLogCollector(App.Services.GetRequiredService<ResourceLoggerService>(), options.ExpectedHealthy);
            await App.StartAsync().ConfigureAwait(false);

            await WaitForExpectedHealthyAsync(options).ConfigureAwait(false);

            _scriptHost = await ConnectScriptHostAsync(options.ClientResource).ConfigureAwait(false);
            _grains = _scriptHost.Services.GetRequiredService<IGrainFactory>();
        }
        catch
        {
            // xunit never calls DisposeAsync when InitializeAsync throws, which leaked the
            // session containers on every failed boot. Run the normal cleanup path here instead.
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup must not replace the original boot failure.
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_logCollector is not null)
        {
            await _logCollector.DisposeAsync().ConfigureAwait(false);
            _logCollector = null;
        }

        if (_scriptHost is not null)
        {
            await _scriptHost.StopAsync().ConfigureAwait(false);
            _scriptHost.Dispose();
            _scriptHost = null;
        }

        if (App is not null)
        {
            await App.DisposeAsync().ConfigureAwait(false);
            App = null!;
        }
    }

    public IDigitalBrain BrainFor(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_grains is null)
        {
            throw new InvalidOperationException(
                $"{nameof(BrainFor)} was called before {nameof(InitializeAsync)} completed.");
        }

        return DigitalBrainClient.Connect(_grains, owner);
    }

    public HttpClient CreateHttpClient(string resource, string? endpointName = null)
        => App.CreateHttpClient(resource, endpointName);

    private static void StubParameters(IDistributedApplicationTestingBuilder appBuilder)
    {
        foreach (var parameter in appBuilder.Resources.OfType<ParameterResource>())
        {
            appBuilder.Configuration[$"Parameters:{parameter.Name}"] = "test";
        }
    }

    // TripRadar's container-isolation pattern: never reuse a container, its name, or its volume
    // state across test runs. A ground-truth model probe proved "storage"
    // (the Azurite emulator) has CLR type AzureStorageResource, not ContainerResource — so this
    // walks IsContainer() (carries a ContainerImageAnnotation, the same test Aspire's own
    // ContainerResourceExtensions.IsContainer uses) rather than OfType<ContainerResource>(), and
    // sets every annotation directly rather than through WithLifetime<T>/WithContainerName<T>,
    // both of which are constrained to `where T : ContainerResource` and would silently skip
    // "storage" again. Without this, "storage" would keep its production
    // ContainerLifetimeAnnotation=Persistent and ContainerMountAnnotation, mounting the
    // developer's dev data volume into the test run and writing test data into it.
    private static void IsolateContainers(IDistributedApplicationTestingBuilder appBuilder)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];

        foreach (var resource in appBuilder.Resources.Where(resource => resource.IsContainer()))
        {
            foreach (var lifetime in resource.Annotations.OfType<ContainerLifetimeAnnotation>().ToList())
            {
                resource.Annotations.Remove(lifetime);
            }

            // Aspire's lifetime decision (ResourceExtensions.GetLifetimeType) consults
            // PersistenceAnnotation before ContainerLifetimeAnnotation, and production
            // WithLifetime(ContainerLifetime.Persistent) adds both — so unless the persistence
            // annotation goes too, the container outlives the test session (a live run proved it:
            // storage-e2e-* survived a fully green run and teardown). The annotation type is
            // [Experimental("ASPIREPERSISTENCE001")] and cannot be named here without opting into
            // that surface, so it is matched by its stable full name instead.
            foreach (var persistence in resource.Annotations
                         .Where(annotation => annotation.GetType().FullName == "Aspire.Hosting.ApplicationModel.PersistenceAnnotation")
                         .ToList())
            {
                resource.Annotations.Remove(persistence);
            }

            resource.Annotations.Add(new ContainerLifetimeAnnotation { Lifetime = ContainerLifetime.Session });

            if (resource.TryGetContainerMounts(out var mounts))
            {
                foreach (var mount in mounts.ToList())
                {
                    resource.Annotations.Remove(mount);
                }
            }

            foreach (var containerName in resource.Annotations.OfType<ContainerNameAnnotation>().ToList())
            {
                resource.Annotations.Remove(containerName);
            }

            resource.Annotations.Add(new ContainerNameAnnotation { Name = $"{resource.Name}-e2e-{runId}" });
        }
    }

    // Proxyless project endpoints need isolation too. Aspire allocates their port
    // and supplies it to Kestrel; callers use CreateHttpClient/service discovery.
    private static void RandomizePorts(IDistributedApplicationTestingBuilder appBuilder)
    {
        foreach (var resource in appBuilder.Resources)
        {
            if (!resource.TryGetEndpoints(out var endpoints))
            {
                continue;
            }

            foreach (var endpoint in endpoints)
            {
                endpoint.Port = null;
                if (!endpoint.IsProxied && resource is ProjectResource)
                {
                    endpoint.TargetPort = null;
                }
            }
        }
    }

    private static void ArmExplicitStart(IDistributedApplicationTestingBuilder appBuilder, IReadOnlyList<string> explicitStart)
    {
        foreach (var resourceName in explicitStart)
        {
            if (!appBuilder.Resources.TryGetByName(resourceName, out var resource))
            {
                var available = string.Join(", ", appBuilder.Resources.Select(static r => r.Name));
                throw new InvalidOperationException(
                    $"ExplicitStart names unknown resource '{resourceName}'. Available: [{available}].");
            }

            appBuilder.CreateResourceBuilder(resource).WithExplicitStart();
        }
    }

    // kernel's WaitUntilHealthy chain (via .WithReference(brain)) transitively reaches model
    // resources (e.g. the AI module's gemma4-12b) that are children of container resources named
    // in ExplicitStart (e.g. ollama). Since explicit-start resources are deliberately never
    // started, that DCP-level "enter Running" wait blocks forever, upstream of and invisible to
    // WaitForResourceHealthyAsync. Strip every wait that targets an explicit-start resource or a
    // descendant of one, so kernel never blocks on a resource this fixture chose not to start.
    private static void StripNeverStartingWaits(
        IDistributedApplicationTestingBuilder appBuilder,
        IReadOnlyList<string> explicitStart)
    {
        var explicitStartResources = new HashSet<IResource>();
        foreach (var resourceName in explicitStart)
        {
            if (appBuilder.Resources.TryGetByName(resourceName, out var resource))
            {
                explicitStartResources.Add(resource);
            }
        }

        var neverStarting = new HashSet<IResource>(explicitStartResources);
        foreach (var resource in appBuilder.Resources)
        {
            if (ReachesExplicitStart(resource, explicitStartResources))
            {
                neverStarting.Add(resource);
            }
        }

        foreach (var resource in appBuilder.Resources)
        {
            var waitsOnNeverStarting = resource.Annotations
                .OfType<WaitAnnotation>()
                .Where(wait => neverStarting.Contains(wait.Resource))
                .ToList();

            foreach (var wait in waitsOnNeverStarting)
            {
                resource.Annotations.Remove(wait);
            }
        }
    }

    // Walks the resource's parent chain up to the root, reporting whether any ancestor is one of
    // the never-started resources. A ground-truth model probe proved the
    // AI module's gemma4-12b model resource declares its parent purely through
    // IResourceWithParent<T>.Parent (CommunityToolkit's OllamaModelResource) and carries no
    // ResourceRelationshipAnnotation at all, so that interface is checked first; the
    // ResourceRelationshipAnnotation(Type == "Parent") walk — the literal Aspire's own
    // WithParentRelationship embeds for its internal KnownRelationshipTypes.Parent, not itself
    // publicly reachable — remains as a second source for resources that express parentage that way.
    private static bool ReachesExplicitStart(IResource resource, IReadOnlySet<IResource> explicitStartResources)
    {
        var current = resource;
        for (var hop = 0; hop < 32; hop++)
        {
            var parent = ParentOf(current);
            if (parent is null)
            {
                return false;
            }

            if (explicitStartResources.Contains(parent))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static IResource? ParentOf(IResource resource)
    {
        if (resource is IResourceWithParent withParent)
        {
            return withParent.Parent;
        }

        return resource.Annotations
            .OfType<ResourceRelationshipAnnotation>()
            .FirstOrDefault(relationship => string.Equals(relationship.Type, "Parent", StringComparison.Ordinal))
            ?.Resource;
    }

    private static void ArmProjectResources(
        IDistributedApplicationTestingBuilder appBuilder,
        IReadOnlyDictionary<string, string> projectEnvironment)
    {
        foreach (var project in appBuilder.Resources.OfType<ProjectResource>())
        {
            var projectBuilder = appBuilder.CreateResourceBuilder(project).WithBrainTestMode();
            foreach (var (key, value) in projectEnvironment)
            {
                projectBuilder.WithEnvironment(key, value);
            }
        }
    }

    private async Task WaitForExpectedHealthyAsync(BrainE2EOptions options)
    {
        var healthWaits = options.ExpectedHealthy
            .Select(resourceName => App.ResourceNotifications
                .WaitForResourceHealthyAsync(resourceName)
                .WaitAsync(options.HealthTimeout))
            .ToArray();

        try
        {
            await Task.WhenAll(healthWaits).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TimeoutException(DescribeUnhealthyResources(options.ExpectedHealthy), ex);
        }
    }

    private string DescribeUnhealthyResources(IReadOnlyList<string> expectedHealthy)
    {
        var report = new StringBuilder()
            .AppendLine($"Not every expected resource became healthy: {string.Join(", ", expectedHealthy)}.");

        foreach (var resourceName in expectedHealthy)
        {
            report.AppendLine($"--- {resourceName} ---");

            if (App.ResourceNotifications.TryGetCurrentState(resourceName, out var resourceEvent))
            {
                var snapshot = resourceEvent.Snapshot;
                report.AppendLine(
                    $"State={snapshot.State?.Text ?? "(none)"} "
                    + $"ExitCode={snapshot.ExitCode?.ToString() ?? "(none)"} "
                    + $"HealthStatus={snapshot.HealthStatus?.ToString() ?? "(none)"}");
            }
            else
            {
                report.AppendLine("No resource state has been published yet.");
            }

            var lastLines = _logCollector?.LastLines(resourceName, DiagnosticLogLineCount) ?? [];
            if (lastLines.Count == 0)
            {
                report.AppendLine("(no logs captured)");
            }
            else
            {
                foreach (var line in lastLines)
                {
                    report.AppendLine(line);
                }
            }
        }

        return report.ToString();
    }

    // BrainFor needs the shared IGrainFactory for many owners, not a single bound IDigitalBrain,
    // so this builds its own host — reusing RequireStorage and AddDigitalBrainClient so the
    // Orleans wiring itself stays in one place.
    private async Task<IHost> ConnectScriptHostAsync(string clientResource)
    {
        var clustering = await App.GetConnectionStringAsync(DigitalBrainNames.Clustering).ConfigureAwait(false);

        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        hostBuilder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Clustering}"] = clustering;
        foreach (var (configurationKey, value) in await CaptureOrleansClientConfigurationAsync(clientResource).ConfigureAwait(false))
        {
            hostBuilder.Configuration[configurationKey] = value;
        }

        DigitalBrainClientHostingExtensions.RequireStorage(hostBuilder.Configuration);
        hostBuilder.AddDigitalBrainClient(activateOnStart: false);

        var host = hostBuilder.Build();
        try
        {
            await host.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            host.Dispose();
            throw;
        }

        return host;
    }

    // AddDigitalBrainClient registers only keyed Azure clients; UseOrleansClient turns them into
    // clustering/streaming providers purely from the host's "Orleans" configuration section,
    // which Aspire injects into referencing projects as Orleans__* environment variables
    // (AppHost side: WithReference(brain.AsClient())). A freestanding HostApplicationBuilder
    // receives none of that, so Orleans' ClientClusteringValidator throws "Clustering has not
    // been configured". Mirror the section verbatim from the named client-shaped project so
    // ClusterId, ServiceId, and provider service keys match the silo exactly.
    private async Task<IReadOnlyDictionary<string, string>> CaptureOrleansClientConfigurationAsync(string clientResource)
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var executionContext = App.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        var project = model.GetProjectResources()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, clientResource, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No project resource named '{clientResource}' exists in the model; point "
                + $"{nameof(BrainE2EOptions)}.{nameof(BrainE2EOptions.ClientResource)} at the Orleans-client-shaped project.");

        var executionConfiguration = await ExecutionConfigurationBuilder.Create(project)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext)
            .ConfigureAwait(false);
        if (executionConfiguration.Exception is not null)
        {
            throw new InvalidOperationException(
                $"Environment could not be resolved for '{clientResource}': {executionConfiguration.Exception.Message}");
        }

        var orleansConfiguration = executionConfiguration.EnvironmentVariables
            .Where(variable => variable.Key.StartsWith("Orleans__", StringComparison.Ordinal))
            .ToDictionary(
                variable => variable.Key.Replace("__", ":", StringComparison.Ordinal),
                variable => variable.Value,
                StringComparer.Ordinal);

        var isClientShaped = orleansConfiguration.ContainsKey("Orleans:Clustering:ProviderType")
            && !orleansConfiguration.Keys.Any(static key =>
                key.StartsWith("Orleans:Reminders:", StringComparison.Ordinal)
                || key.StartsWith("Orleans:GrainStorage:", StringComparison.Ordinal));
        if (!isClientShaped)
        {
            throw new InvalidOperationException(
                $"'{clientResource}' does not expose Orleans client configuration (an Orleans__Clustering__ProviderType "
                + "environment variable without silo-only Reminders/GrainStorage sections); point "
                + $"{nameof(BrainE2EOptions)}.{nameof(BrainE2EOptions.ClientResource)} at the client-shaped project.");
        }

        return orleansConfiguration;
    }
}
