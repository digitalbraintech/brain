using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly List<Type> _modules = [];
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];
    private readonly Dictionary<Type, object> _states = [];

    internal DigitalBrainBuilder(
        IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<DigitalBrainResource> resource,
        OrleansService orleans,
        IResourceBuilder<AzureBlobStorageResource> durableStateStore,
        IResourceBuilder<AzureBlobStorageResource> grainState)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(durableStateStore);
        ArgumentNullException.ThrowIfNull(grainState);

        ApplicationBuilder = builder;
        Name = name;
        Resource = resource;
        Orleans = orleans;
        DurableStateStore = durableStateStore;
        GrainState = grainState;
    }

    public string Name { get; }

    public IDistributedApplicationBuilder ApplicationBuilder { get; }

    public IResourceBuilder<DigitalBrainResource> Resource { get; }

    internal IResourceBuilder<AzureBlobStorageResource> DurableStateStore { get; }

    internal IResourceBuilder<AzureBlobStorageResource> GrainState { get; }

    internal OrleansService Orleans { get; }

    internal IReadOnlyList<DigitalBrainModuleProjection> Projections => _projections;

    internal IReadOnlyList<IResource> StartupDependencies => _startupDependencies;

    internal IReadOnlyList<Type> Modules => _modules;

    // Set by WithDigitalBrainFakes before projections apply, so a module's projection can skip
    // the operator parameters its fake never reads.
    public bool FakesEnabled { get; internal set; }

    internal void AddModule(Type module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (!_modules.Contains(module))
        {
            _modules.Add(module);
        }
    }

    public TState GetOrAddState<TState>(Func<DigitalBrainBuilder, TState> create, out bool added)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(create);

        if (_states.TryGetValue(typeof(TState), out var existing))
        {
            added = false;
            return (TState)existing;
        }

        var state = create(this);
        _states.Add(typeof(TState), state);
        added = true;
        return state;
    }

    public void AddProjection(DigitalBrainModuleProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (_projections.Any(existing => existing.GetType() == projection.GetType()))
        {
            throw new InvalidOperationException(
                $"{projection.GetType().Name} is already configured on brain '{Name}'. Add it exactly once.");
        }

        _projections.Add(projection);
    }

    internal void RequireHealthyBeforeStart(IResource dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        if (!_startupDependencies.Contains(dependency))
        {
            _startupDependencies.Add(dependency);
        }
    }

    public DigitalBrainClientReference AsClient() => new(this);
}
