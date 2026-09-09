using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static string DurableStateConnectionName => DigitalBrainNames.JournalConnection;

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = builder.AddResource(new DigitalBrainResource(name))
            .ExcludeFromManifest()
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "DigitalBrain",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Running,
                Properties = [new(CustomResourceKnownProperties.Source, "DigitalBrain fabric")],
            });
        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(static emulator => emulator
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent))
            .WithParentRelationship(resource);
        var clustering = storage.AddTables(DigitalBrainNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainNames.Reminders);
        var durableStateStore = storage.AddBlobs(DigitalBrainNames.Journal);
        var grainState = storage.AddBlobs(DigitalBrainNames.GrainState);
        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState);
        var brain = new DigitalBrainBuilder(builder, name, resource, orleans, durableStateStore, grainState);

        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(durableStateStore.Resource);
        brain.RequireHealthyBeforeStart(grainState.Resource);
        return brain;
    }

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
        where TModule : class
        => brain.AddModule<TModule>(static _ => { });

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain, Action<DigitalBrainModuleBuilder<TModule>> configure)
        where TModule : class
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);
        brain.AddModule(typeof(TModule));
        configure(new DigitalBrainModuleBuilder<TModule>(brain));
        return brain;
    }

    public static DigitalBrainBuilder WithDigitalBrainFakes(this DigitalBrainBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        var state = brain.GetOrAddState(static _ => new FakesHostingState(), out var added);
        if (added)
        {
            brain.AddProjection(state);
        }

        state.Enable();
        brain.FakesEnabled = true;
        return brain;
    }

    public static IResourceBuilder<T> WithDigitalBrainFakes<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEnvironment("DigitalBrain__Fakes__Enabled", "true");
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        builder.WithReference(brain.Orleans);
        builder.WithReference(brain.DurableStateStore, DigitalBrainNames.JournalConnection);
        builder.WithReference(brain.GrainState, DigitalBrainNames.GrainState);

        for (var index = 0; index < brain.Modules.Count; index++)
        {
            var module = brain.Modules[index];
            builder.WithEnvironment(
                $"DigitalBrain__Modules__{index}",
                $"{module.FullName}, {module.Assembly.GetName().Name}");
        }

        WaitUntilHealthy(builder, brain.StartupDependencies);

        foreach (var projection in brain.Projections)
        {
            projection.Apply(builder);
        }

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainClientReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.WithReference(client.Brain.Orleans.AsClient());
        WaitUntilHealthy(builder, client.Brain.StartupDependencies);
        return builder;
    }

    private static void WaitUntilHealthy<TResource>(
        IResourceBuilder<TResource> builder,
        IReadOnlyList<IResource> dependencies)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        foreach (var dependency in dependencies)
        {
            builder.WithAnnotation(new WaitAnnotation(dependency, WaitType.WaitUntilHealthy, exitCode: 0));
        }
    }

    private sealed class FakesHostingState : DigitalBrainModuleProjection
    {
        private bool _enabled;

        internal void Enable() => _enabled = true;

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled)
            {
                return;
            }

            builder.WithEnvironment("DigitalBrain__Fakes__Enabled", "true");
        }
    }
}
