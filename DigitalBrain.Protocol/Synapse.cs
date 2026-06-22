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
public record DemoMessageSynapse(string Text) : Synapse(nameof(DemoMessageSynapse), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ExperienceUsed(string Pack, string Action) : Synapse(nameof(ExperienceUsed), DateTimeOffset.UtcNow);

// Core system neuron interfaces (everything is a Neuron)
public interface IAspire : INeuron, IHandle<StartDistributedApp>, IHandle<RestartResource> { }

public interface IMarketplace : INeuron, IHandle<PublishToMarketplace>, IHandle<InstallFromMarketplace>, IHandle<ListPublished>;

public interface ICompiler : INeuron, IHandle<CreateNeuronRequest> { }

public interface IAspireNeuron : IAspire { }

public interface IMarketplaceNeuron : IMarketplace { }

public interface IMetaOptimizerNeuron : INeuron, IHandle<NeuronTelemetry>, IHandle<WiringOptimizationProposed> { }

public interface IGeneratedNeuron : INeuron { }

public interface ILlmModel { }

public sealed class Qwen : ILlmModel { }

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false)]
public sealed class LLMAttribute<T> : Attribute where T : ILlmModel { }

[GenerateSerializer]
public record LlmPrompt(string Prompt, string? PreferredModel = null) : Synapse(nameof(LlmPrompt), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record LlmResponse(string Prompt, string Response, string ModelUsed) : Synapse(nameof(LlmResponse), DateTimeOffset.UtcNow);

[LLM<Qwen>]
public interface ILlmNeuron : INeuron, IHandle<LlmPrompt> { }

// Awesome Software Engineering domain - testing two teams creating simple apps
[GenerateSerializer]
public record CreateSimpleApp(string Team, string Description, string Language = "csharp") : Synapse(nameof(CreateSimpleApp), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record SimpleAppCreated(string Team, string AppName, string GeneratedCode) : Synapse(nameof(SimpleAppCreated), DateTimeOffset.UtcNow);

public interface ISoftwareEngineeringTeam : INeuron, IHandle<CreateSimpleApp> { }

[LLM<Qwen>]
public interface ISoftware20Team : ISoftwareEngineeringTeam { }

public interface ISoftware10Team : ISoftwareEngineeringTeam { }

// Kernel task grain contract.
public interface IKernelTask : INeuron, IHandle<RunKernelTask>, IHandle<CancelKernelTask>
{
    Task<KernelTaskInfo> GetInfoAsync();
}

public interface IInoNeuron : INeuron, IHandle<InoRequest>
{
    Task<string> AskAsync(string prompt);
}

// Self-awareness: SystemStatus + proposals (MVP for auto diagnose + simulate fix)
[GenerateSerializer]
public record SystemLaunched(string SystemName, DateTimeOffset Timestamp) : Synapse(nameof(SystemLaunched), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record SystemStatusChanged(string Component, string Status, string? Details = null) : Synapse(nameof(SystemStatusChanged), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record FixProposal(string Issue, string ProposedFix, string From) : Synapse(nameof(FixProposal), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record SimulationResult(string Scenario, bool Success, string Details) : Synapse(nameof(SimulationResult), DateTimeOffset.UtcNow);

public interface ISystemStatus : INeuron, IHandle<SystemStatusChanged>, IHandle<FixProposal> { }

// Demo / test specific (promoted to contracts for cross-project test + sample usage)
public interface IDemoNeuron : INeuron
{
    Task<string> GetLastMessageAsync();
}

// === Real Marketplace Pack Model (core to fixing the blocker) ===
// A NeuroPack is the distributable unit: metadata + code + ownership + monetization info.
// This enables private marketplace + commissions.
[GenerateSerializer]
public record NeuroPack(
    [property: Id(0)] string Name,
    [property: Id(1)] string Version,
    [property: Id(2)] string OwnerId = "anonymous",
    [property: Id(3)] bool IsPrivate = false,
    [property: Id(4)] double CommissionRate = 0.10, // 10% default commission taken by marketplace
    [property: Id(5)] string Code = "",
    [property: Id(6)] string Description = ""
);

// Richer publish/install commands that carry full pack data for real marketplace behavior.
// Old simple constructors still work via defaults for minimal compat during transition.
[GenerateSerializer]
public record PublishToMarketplace(
    string PackName, 
    string Version, 
    string Code = "", 
    string OwnerId = "anonymous", 
    bool IsPrivate = false, 
    double CommissionRate = 0.10,
    string Description = ""
) : Synapse(nameof(PublishToMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InstallFromMarketplace(
    string PackName, 
    string Version, 
    string BuyerId = "anonymous"
) : Synapse(nameof(InstallFromMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record NeuroPackInstalled(NeuroPack Pack) : Synapse(nameof(NeuroPackInstalled), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record PublishedList(IReadOnlyList<NeuroPack> Packs) : Synapse(nameof(PublishedList), DateTimeOffset.UtcNow);

// Commission event - fired on successful install to support marketplace economics
[GenerateSerializer]
public record CommissionTaken(
    string PackName, 
    string Version, 
    string BuyerId, 
    string SellerId, 
    double CommissionRate, 
    double CommissionAmount
) : Synapse(nameof(CommissionTaken), DateTimeOffset.UtcNow);

// Kernel primitives: dual journal checkpoints + branching for simulation / time travel.
[GenerateSerializer]
public record Checkpoint(NeuronId Source, IReadOnlyList<Synapse> Snapshot, DateTimeOffset TakenAt) : Synapse(nameof(Checkpoint), TakenAt);

[GenerateSerializer]
public record BranchCreated(NeuronId Source, string BranchId) : Synapse(nameof(BranchCreated), DateTimeOffset.UtcNow);

// Kernel Tasks: first-class self-recoverable units (lifecycle via journal).
[GenerateSerializer]
public record KernelTaskCreated(string TaskId, string Description) : Synapse(nameof(KernelTaskCreated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record KernelTaskStarted(string TaskId) : Synapse(nameof(KernelTaskStarted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record KernelTaskProgress(string TaskId, string Detail) : Synapse(nameof(KernelTaskProgress), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record KernelTaskCompleted(string TaskId, string? Result = null) : Synapse(nameof(KernelTaskCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record KernelTaskCancelled(string TaskId) : Synapse(nameof(KernelTaskCancelled), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record RunKernelTask(string TaskId, string Description) : Synapse(nameof(RunKernelTask), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CancelKernelTask(string TaskId) : Synapse(nameof(CancelKernelTask), DateTimeOffset.UtcNow);

// Rich task state for INO / kernel consumers (value result + status).
[GenerateSerializer]
public record KernelTaskInfo(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Status,
    [property: Id(2)] string? Result = null
);

// INO - the personal ultra-context assistant living in the kernel.
[GenerateSerializer]
public record InoRequest(string Prompt, string? SessionId = null) : Synapse(nameof(InoRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InoResponse(string Prompt, string Response, string[] UsedTaskIds) : Synapse(nameof(InoResponse), DateTimeOffset.UtcNow);

// For INO excellent long-term/multi-scale context (summaries from journals).
[GenerateSerializer]
public record MemorySummary(string Topic, string Summary, DateTimeOffset At) : Synapse(nameof(MemorySummary), At);

// INO Code Editor neuron - for visual editing and execution of INO code
[GenerateSerializer]
public record InoCodeEdit(string EditorId, string Code, string Language = "ino") : Synapse(nameof(InoCodeEdit), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InoCodeRun(string EditorId, string Result) : Synapse(nameof(InoCodeRun), DateTimeOffset.UtcNow);

public interface IInoCodeEditor : INeuron, IHandle<InoCodeEdit>, IHandle<InoCodeRun> { }

// Smart ContextNeuron for INO - manages chat, agent, filter, cluster contexts like context providers
[GenerateSerializer]
public record ContextUpdate(string ContextName, string Key, string Value) : Synapse(nameof(ContextUpdate), DateTimeOffset.UtcNow);

public interface IContextNeuron : INeuron, IHandle<ContextUpdate>
{
    Task<string> GetContextAsync(string contextName);
}

// Dynamic DB support neuron with typed synapses (inspired by .NET 11 Preview 5 EF/file-based + runtime dynamic)
[GenerateSerializer]
public record DbConnect(string ConnectionName, string Provider, string ConnectionString) : Synapse(nameof(DbConnect), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record DbQuery(string ConnectionName, string Query, string? Result = null) : Synapse(nameof(DbQuery), DateTimeOffset.UtcNow);

public interface IDbSupportNeuron : INeuron, IHandle<DbConnect>, IHandle<DbQuery> { }

// Filter changes - INO/Context must be notified so assistant knows current UI view state
[GenerateSerializer]
public record FilterChanged(string View, string Filter, string Value) : Synapse(nameof(FilterChanged), DateTimeOffset.UtcNow);

// 3D graph / cluster observation synapses
[GenerateSerializer]
public record ClusterActivity(string NodeId, string Activity, double Value) : Synapse(nameof(ClusterActivity), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ThreeDGraphUpdate(string GraphId, string DataJson) : Synapse(nameof(ThreeDGraphUpdate), DateTimeOffset.UtcNow);
