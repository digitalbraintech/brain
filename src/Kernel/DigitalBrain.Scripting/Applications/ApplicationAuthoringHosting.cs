using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace DigitalBrain.Scripting.Applications;

public static class ApplicationAuthoringHosting
{
    public static IServiceCollection AddApplicationAuthoring(this IServiceCollection services, string storeRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        services.TryAddSingleton(provider =>
        {
            var endpoint = provider.GetRequiredService<IOptions<EndpointOptions>>().Value;
            var cluster = provider.GetRequiredService<IOptions<ClusterOptions>>().Value;
            return ApplicationWorkerBootstrapServer.Start(new(
                endpoint.AdvertisedIPAddress.ToString(), endpoint.GatewayPort,
                cluster.ClusterId, cluster.ServiceId),
                provider.GetRequiredService<ApplicationWorkerCapabilityAuthority>());
        });
        services.TryAddSingleton(provider => new ApplicationArtifactHost(
            provider.GetRequiredService<ApplicationWorkerBootstrapServer>()));
        services.TryAddSingleton(provider => new ApplicationAuthoringService(
            storeRoot,
            host: provider.GetRequiredService<ApplicationArtifactHost>(),
            logger: provider.GetRequiredService<ILogger<ApplicationAuthoringService>>(),
            scenarioDrivers: provider.GetServices<IApplicationScenarioDriver>(),
            neuronCapabilities: provider.GetServices<ApplicationNeuronCapabilityRegistration>(),
            neuronInputs: provider.GetServices<ApplicationNeuronInputRegistration>(),
            neuronEvents: provider.GetServices<ApplicationNeuronEventRegistration>()));
        services.TryAddSingleton<IApplicationAuthoring>(provider =>
            provider.GetRequiredService<ApplicationAuthoringService>());
        services.AddHostedService<ApplicationArtifactSupervisor>();
        return services;
    }
}

internal sealed class ApplicationArtifactSupervisor(
    ApplicationAuthoringService authoring,
    IGrainFactory grains,
    ApplicationArtifactHost host,
    ILogger<ApplicationArtifactSupervisor> logger) : BackgroundService
{
    private readonly Dictionary<string, Worker> workers = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(250, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var worker in workers.Values)
            {
                await worker.StopAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var desired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in await authoring.ReadSupervisorRegistrationsAsync(cancellationToken)
                     .ConfigureAwait(false))
        {
            try
            {
                var actor = new ActorContext(new PrincipalId(registration.Principal), "application-supervisor");
                await using var brain = DigitalBrainClient.Connect(grains, registration.Owner, actor);
                using var actorScope = VerifiedActor.Enter(actor);
                await authoring.CompletePendingActivationAsync(brain, registration, cancellationToken)
                    .ConfigureAwait(false);
                var pending = await brain.Application(registration.Key).PendingRevisionsAsync(cancellationToken)
                    .ConfigureAwait(false);
                var revisions = pending.ToHashSet(StringComparer.Ordinal);
                if (registration.PendingActivation is { } pendingActivation)
                {
                    revisions.Add(pendingActivation.RevisionId);
                }
                if (registration.ActiveArtifact is { } active)
                {
                    revisions.Add(active.ApplicationRevision);
                }

                foreach (var revision in revisions)
                {
                    if (!registration.Artifacts.TryGetValue(revision, out var artifact) &&
                        registration.ActiveArtifact?.ApplicationRevision == revision)
                    {
                        artifact = registration.ActiveArtifact;
                    }
                    if (artifact is null)
                    {
                        continue;
                    }
                    var workerKey = $"{registration.Owner}/{registration.Principal:n}/{registration.Key}/{revision}";
                    desired.Add(workerKey);
                    if (workers.TryGetValue(workerKey, out var completed) && completed.IsCompleted)
                    {
                        workers.Remove(workerKey);
                        await completed.StopAsync().ConfigureAwait(false);
                    }
                    if (!workers.ContainsKey(workerKey))
                    {
                        workers[workerKey] = Worker.Start(
                            grains, registration, actor, artifact, host, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Application supervisor failed to reconcile {Owner}/{ApplicationKey}; other applications will continue.",
                    registration.Owner, registration.Key);
            }
        }

        foreach (var stale in workers.Keys.Where(key => !desired.Contains(key)).ToArray())
        {
            var worker = workers[stale];
            workers.Remove(stale);
            await worker.StopAsync().ConfigureAwait(false);
        }
    }

    private sealed class Worker(CancellationTokenSource stopping, Task task)
    {
        internal bool IsCompleted => task.IsCompleted;

        internal static Worker Start(
            IGrainFactory grains,
            SupervisorRegistration registration,
            ActorContext actor,
            FileApplicationArtifact artifact,
            ApplicationArtifactHost host,
            CancellationToken supervisorToken)
        {
            var stopping = CancellationTokenSource.CreateLinkedTokenSource(supervisorToken);
            return new(stopping, RunAsync(grains, registration, actor, artifact, host, stopping.Token));
        }

        private static async Task RunAsync(
            IGrainFactory grains,
            SupervisorRegistration registration,
            ActorContext actor,
            FileApplicationArtifact artifact,
            ApplicationArtifactHost host,
            CancellationToken cancellationToken)
        {
            await using var brain = DigitalBrainClient.Connect(grains, registration.Owner, actor);
            using var actorScope = VerifiedActor.Enter(actor);
            await host.ServeAsync(
                artifact, brain, registration.Key, cancellationToken).ConfigureAwait(false);
        }

        internal async Task StopAsync()
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // Reconciliation restarts failed revision workers while they remain required.
            }
            finally
            {
                stopping.Dispose();
            }
        }
    }
}
