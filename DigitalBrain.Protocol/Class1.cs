global using Orleans;
global using Orleans.Runtime;

namespace DigitalBrain.Protocol;

[GenerateSerializer]
public record Synapse(
    [property: Id(0)] string Type,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] NeuronId? Sender = null,
    [property: Id(3)] NeuronId? Receiver = null,
    [property: Id(4)] bool IsBroadcast = false,
    [property: Id(5)] string? CorrelationId = null
)
{
    public Synapse Stamp(NeuronId sender) =>
        this with { Sender = sender, Timestamp = DateTimeOffset.UtcNow };
}

[GenerateSerializer]
public record NeuronId([property: Id(0)] string Value)
{
    public static implicit operator string(NeuronId id) => id.Value;
    public override string ToString() => Value;
}

public interface INeuron : IGrainWithStringKey
{
    ValueTask FireAsync<T>(T payload) where T : Synapse;
    Task<IReadOnlyList<Synapse>> GetTimelineAsync();
    Task DeliverAsync(Synapse synapse);
}

[GenerateSerializer]
public record NeuronActivated(NeuronId Neuron) : Synapse(nameof(NeuronActivated), DateTimeOffset.UtcNow);

public interface IHandle<T> where T : Synapse
{
    Task HandleAsync(T synapse);
}

// Command / event synapses for core system neurons (per v2 spec)
[GenerateSerializer]
public record StartDistributedApp(string AppName) : Synapse(nameof(StartDistributedApp), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record RestartResource(string ResourceName) : Synapse(nameof(RestartResource), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record DistributedAppStarted(string AppName, bool Success, string? Details = null) : Synapse(nameof(DistributedAppStarted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record PublishToMarketplace(string PackName, string Version) : Synapse(nameof(PublishToMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InstallFromMarketplace(string PackName, string Version) : Synapse(nameof(InstallFromMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record NeuroPackInstalled(string PackName, string Version) : Synapse(nameof(NeuroPackInstalled), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ListPublished() : Synapse(nameof(ListPublished), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CreateNeuronRequest(string Description, string Language = "csharp") : Synapse(nameof(CreateNeuronRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record NeuronCodeGenerated(string Description, string GeneratedCodeSnippet) : Synapse(nameof(NeuronCodeGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record NeuronTelemetry(NeuronId Neuron, string Event, int Count = 1) : Synapse(nameof(NeuronTelemetry), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record WiringOptimizationProposed(string Proposal, string FromNeuron) : Synapse(nameof(WiringOptimizationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record PublishedList(IReadOnlyList<string> Packs) : Synapse(nameof(PublishedList), DateTimeOffset.UtcNow);

// Core system neuron interfaces (everything is a Neuron)
public interface IAspire : INeuron, IHandle<StartDistributedApp>, IHandle<RestartResource> { }

public interface IMarketplace : INeuron, IHandle<PublishToMarketplace>, IHandle<InstallFromMarketplace>, IHandle<ListPublished> { }

public interface ICompiler : INeuron, IHandle<CreateNeuronRequest> { }
