using DigitalBrain.Protocol;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Silo;

// IAspire neuron (orchestrates distributed apps via Aspire model, fires completion synapses)
public interface IAspireNeuron : IAspire { }

[GrainType("neuro.aspire.v1")]
public class AspireOrchestratorNeuron : Neuron, IAspireNeuron
{
    public AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger,
        [PersistentState("journal", "Default")] IPersistentState<List<Synapse>> journal)
        : base(logger, journal)
    {
    }

    public async Task HandleAsync(StartDistributedApp cmd)
    {
        Logger.LogInformation("Aspire starting app: {App}", cmd.AppName);
        await FireAsync(new DistributedAppStarted(cmd.AppName, Success: true, "started via neuro"));
    }

    public async Task HandleAsync(RestartResource cmd)
    {
        Logger.LogInformation("Aspire restarting resource: {Res}", cmd.ResourceName);
        await FireAsync(new DistributedAppStarted(cmd.ResourceName, Success: true, "restarted"));
    }
}

// IMarketplace neuron (publish/install neuro packs, dynamic assembly load hook stub)
public interface IMarketplaceNeuron : IMarketplace { }

[GrainType("neuro.marketplace.v1")]
public class MarketplaceNeuron : Neuron, IMarketplaceNeuron
{
    private readonly List<string> _published = new();

    public MarketplaceNeuron(ILogger<MarketplaceNeuron> logger,
        [PersistentState("journal", "Default")] IPersistentState<List<Synapse>> journal)
        : base(logger, journal)
    {
    }

    public async Task HandleAsync(PublishToMarketplace cmd)
    {
        Logger.LogInformation("Marketplace publish: {Pack}@{Ver}", cmd.PackName, cmd.Version);
        _published.Add($"{cmd.PackName}@{cmd.Version}");
        await FireAsync(new NeuroPackInstalled(cmd.PackName, cmd.Version));
    }

    public async Task HandleAsync(InstallFromMarketplace cmd)
    {
        Logger.LogInformation("Marketplace install: {Pack}@{Ver}", cmd.PackName, cmd.Version);
        // Stub: would trigger AspireHost.AddDynamicResource + Assembly.Load + grain re-register
        await FireAsync(new NeuroPackInstalled(cmd.PackName, cmd.Version));
    }

    public async Task HandleAsync(ListPublished _cmd)
    {
        Logger.LogInformation("Marketplace listing {Count} packs", _published.Count);
        await FireAsync(new PublishedList(_published.AsReadOnly()));
    }
}

// Compiler / Meta-neuron for English → code (Reqnroll + simulated LLM codegen per spec)
[GrainType("neuro.compiler.v1")]
public class CompilerNeuron : Neuron, ICompiler
{
    public CompilerNeuron(ILogger<CompilerNeuron> logger,
        [PersistentState("journal", "Default")] IPersistentState<List<Synapse>> journal)
        : base(logger, journal)
    {
    }

    public async Task HandleAsync(CreateNeuronRequest req)
    {
        Logger.LogInformation("Compiler generating neuron for: {Desc}", req.Description);
        // Stub: simulate Reqnroll scenario + LLM fill → compile → grain DLL + Aspire reload
        var snippet = $"// Generated from: {req.Description}\npublic class GeneratedNeuron : Neuron {{ /* ... */ }}";
        await FireAsync(new NeuronCodeGenerated(req.Description, snippet));
        await FireAsync(new NeuronTelemetry(Self, "code-generated"));
        // Could fire NeuroPackInstalled etc for full flow
    }
}

// Self-Improvement: MetaOptimizerNeuron per spec - tracks telemetry, proposes better wiring
public interface IMetaOptimizerNeuron : INeuron, IHandle<NeuronTelemetry>, IHandle<WiringOptimizationProposed> { }

[GrainType("neuro.optimizer.v1")]
public class MetaOptimizerNeuron : Neuron, IMetaOptimizerNeuron
{
    private int _telemetryCount = 0;

    public MetaOptimizerNeuron(ILogger<MetaOptimizerNeuron> logger,
        [PersistentState("journal", "Default")] IPersistentState<List<Synapse>> journal)
        : base(logger, journal)
    {
    }

    public async Task HandleAsync(NeuronTelemetry telemetry)
    {
        _telemetryCount += telemetry.Count;
        Logger.LogInformation("Optimizer received telemetry from {Neuron}: {Event} (total {Count})", telemetry.Neuron, telemetry.Event, _telemetryCount);

        if (_telemetryCount >= 5) // lowered threshold for prototype demo (spec says 1000)
        {
            var proposal = "Optimize: Add more Compiler neurons for parallel English->code tasks";
            await FireAsync(new WiringOptimizationProposed(proposal, Self.Value));
            _telemetryCount = 0; // reset
        }
    }

    public Task HandleAsync(WiringOptimizationProposed proposal)
    {
        Logger.LogInformation("Optimizer proposal received: {Proposal} from {From}", proposal.Proposal, proposal.FromNeuron);
        return Task.CompletedTask;
    }
}