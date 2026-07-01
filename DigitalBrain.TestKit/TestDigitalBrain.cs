using DigitalBrain.Core;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public sealed class TestDigitalBrain : IDigitalBrain, IAsyncLifetime
{
    private readonly Action<ISiloBuilder>? _extend;
    private TestCluster? _cluster;

    public TestDigitalBrain(Action<ISiloBuilder>? extend = null) => _extend = extend;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();

        // AddSiloBuilderConfigurator<T>() requires a parameterless T: Orleans stores T's
        // AssemblyQualifiedName and reflectively Activator.CreateInstance()s it inside the
        // test host process, so a closure-capturing ISiloConfigurator instance can't be passed
        // directly. Bridge the captured `_extend` delegate through an AsyncLocal that
        // ExtendSiloConfigurator reads when Orleans reflectively constructs it during
        // builder.Build()/DeployAsync() below, on this same async flow.
        if (_extend is not null)
        {
            builder.AddSiloBuilderConfigurator<ExtendSiloConfigurator>();
            ExtendSiloConfigurator.Current.Value = _extend;
        }

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        _cluster!.GrainFactory.GetGrain<TGrain>(key);

    public Task FireAsync<T>(T synapse) where T : Synapse =>
        Grain<INeuron>(synapse.SynapseId.ToString()).DeliverAsync(synapse);

    public Task DeliverAsync<T>(T synapse) where T : Synapse =>
        synapse.Receiver is { } r
            ? Grain<INeuron>(r.Value).DeliverAsync(synapse)
            : throw new InvalidOperationException("DeliverAsync requires synapse.Receiver to be set.");

    private sealed class ExtendSiloConfigurator : ISiloConfigurator
    {
        public static readonly AsyncLocal<Action<ISiloBuilder>?> Current = new();

        public void Configure(ISiloBuilder siloBuilder) => Current.Value?.Invoke(siloBuilder);
    }
}
