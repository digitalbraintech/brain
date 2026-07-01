namespace DigitalBrain.Core;

[GenerateSerializer]
public readonly record struct SynapseType([property: Id(0)] string Value);

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
    [Id(6)] public string SynapseId { get; init; } = Guid.NewGuid().ToString("N");

    [Id(7)] public string? CausationId { get; init; }

    public Synapse Stamp(NeuronId sender, Synapse? cause = null) =>
        this with
        {
            Sender = sender,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = CorrelationId ?? cause?.CorrelationId ?? cause?.SynapseId ?? SynapseId,
            CausationId = cause?.SynapseId
        };
}

[GenerateSerializer]
public record FilterMarketplace(
    [property: Id(0)] string? Tier = null,
    [property: Id(1)] string? Channel = null
) : Synapse(nameof(FilterMarketplace), DateTimeOffset.UtcNow);

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
public record ExperienceUsed(
    string Pack,
    string Action,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(ExperienceUsed), DateTimeOffset.UtcNow);

// Core system neuron interfaces (everything is a Neuron)
public interface IAspire : INeuron, IHandle<StartDistributedApp>, IHandle<RestartResource> { }

public interface IMarketplace : INeuron, IHandle<PublishToMarketplace>, IHandle<InstallFromMarketplace>, IHandle<ListPublished>, IHandle<FilterMarketplace>;

public interface ICompiler : INeuron, IHandle<CreateNeuronRequest> { }

public interface IAspireNeuron : IAspire { }

public interface IMarketplaceNeuron : IMarketplace { }

// Thin common marker for channel neurons (Telegram, Flutter UI, etc.) per item 13.
// Allows discovery and shared patterns (e.g. CorrelationId/CausationId for reply context across channels).
// No methods yet – keeps it thin; specific contracts now live in their peer ino projects
// (ITelegramChatNeuron in DigitalBrain.Telegram.Channel, IFlutterUiNeuron in DigitalBrain.UiKit), not Core.
public interface IChannelNeuron : INeuron
{
}

// IUser contract lives in Core so kernel can run standalone for security/air-gapped scenarios.
// Full user accounts, auth, billing live in the private marketplace service.
[GenerateSerializer]
public readonly record struct UserId([property: Id(0)] string Value)
{
    public static UserId Anonymous => new("anonymous");
}

[GenerateSerializer]
public record LoginRequest(
    string Username,
    string Password,
    string ClientId = "flutter") : Synapse(nameof(LoginRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record LoginSucceeded(
    UserId UserId,
    string SessionId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string ClientId) : Synapse(nameof(LoginSucceeded), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record LoginFailed(
    string Username,
    string Reason,
    string ClientId) : Synapse(nameof(LoginFailed), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record LogoutRequest(
    string SessionId,
    string ClientId = "flutter") : Synapse(nameof(LogoutRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record UserSessionCreated(
    UserId UserId,
    string SessionId,
    DateTimeOffset ExpiresAt,
    string ClientId) : Synapse(nameof(UserSessionCreated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record UserSessionEnded(
    string SessionId,
    string ClientId) : Synapse(nameof(UserSessionEnded), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record LocalUserRegistered(
    UserId UserId,
    string Username,
    string DisplayName,
    string PasswordHashBase64,
    string PasswordSaltBase64,
    IReadOnlyList<string> Roles) : Synapse(nameof(LocalUserRegistered), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record UserSessionState(
    UserId UserId,
    string SessionId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    DateTimeOffset ExpiresAt,
    bool Active);

public interface IUserSessionNeuron : INeuron, IHandle<LoginRequest>, IHandle<LogoutRequest>
{
    Task<UserSessionState?> GetSessionAsync(string sessionId);
    Task<UiSurface> BuildLoginSurfaceAsync(string? clientId = null);
}

public interface IUserGrain : IGrainWithStringKey
{
    Task<UserProfile> GetProfileAsync();
    Task<bool> HasEntitlementAsync(string bundleOrResource, string actionOrCapability);
}

[GenerateSerializer]
public record UserProfile(UserId Id, string DisplayName, IReadOnlyList<string> Roles);

// Remote client contract for the private marketplace service (new repo).
// Kernel's MarketplaceNeuron becomes a thin proxy when RemoteMarketplaceBaseUrl is configured.
// This keeps local stub mode for security/air-gapped while enabling cloud pay-go distribution.
public interface IRemoteMarketplaceClient
{
    Task PublishAsync(PublishToMarketplace cmd);
    Task InstallAsync(InstallFromMarketplace cmd);
    Task<PublishedList> ListAsync();
    // Security policy, user entitlement queries etc. added as the private service is built.
}

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
    [property: Id(6)] string Description = "",
    // Trust chain: author's ECDSA public key (SPKI, base64) + signature over Name|Version|Hash(Code)|PubKey.
    // Empty = unsigned. Signed via PackSignatureVerifier.SignPack at publish, verified at install.
    [property: Id(7)] string AuthorPublicKeyBase64 = "",
    [property: Id(8)] string SignatureBase64 = "",
    // Economics: price in the marketplace currency. 0 = free. Premium (>0) packs require a license at install.
    [property: Id(9)] decimal Price = 0m,
    [property: Id(10)] BundleManifest? Manifest = null
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
    string Description = "",
    string AuthorPublicKeyBase64 = "",
    string SignatureBase64 = "",
    decimal Price = 0m
) : Synapse(nameof(PublishToMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InstallFromMarketplace(
    string PackName, 
    string Version, 
    string BuyerId = "anonymous",
    string? SessionId = null
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
) : Synapse(nameof(CommissionTaken), default);  // Timestamp set by Stamp() on Fire path for consistent lineage.

// Dual journal checkpoints + branching for simulation / time travel.
[GenerateSerializer]
public record Checkpoint(NeuronId Source, IReadOnlyList<Synapse> Snapshot, DateTimeOffset TakenAt) : Synapse(nameof(Checkpoint), TakenAt);

[GenerateSerializer]
public record BranchCreated(NeuronId Source, string BranchId) : Synapse(nameof(BranchCreated), DateTimeOffset.UtcNow);

// Task protocol messages (recoverable task lifecycle for INO, MCP actions, orchestration).
// The durable grain impl (IKernelTask) lives in the kernel layer; these messages are universal core protocol.
[GenerateSerializer]
public record TaskCreated(TaskId TaskId, string Description) : Synapse(nameof(TaskCreated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record TaskStarted(TaskId TaskId) : Synapse(nameof(TaskStarted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record TaskProgress(TaskId TaskId, string Detail) : Synapse(nameof(TaskProgress), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record TaskCompleted(TaskId TaskId, string? Result = null) : Synapse(nameof(TaskCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record TaskCancelled(TaskId TaskId) : Synapse(nameof(TaskCancelled), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record RunTask(
    TaskId TaskId,
    string Description,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(RunTask), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CancelTask(
    TaskId TaskId,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(CancelTask), DateTimeOffset.UtcNow);

// Rich task state returned by the task grain.
[GenerateSerializer]
public record TaskInfo(
    [property: Id(0)] TaskId TaskId,
    [property: Id(1)] string Status,
    [property: Id(2)] string? Result = null
);

// INO - the personal ultra-context assistant.
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

// NuGet + Roslyn architect for closed loops (SEClosedLoopNeuron).
[GenerateSerializer]
public record NuGetCommand(string Action, string Target, string Args = "") : Synapse(nameof(NuGetCommand), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record NuGetResult(string Target, bool Success, string Output) : Synapse(nameof(NuGetResult), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ArchitectRequest(string Path, string Task) : Synapse(nameof(ArchitectRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ArchitectReport(string Path, string Report) : Synapse(nameof(ArchitectReport), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ArchitectResult(string Path, string Result, string Task) : Synapse(nameof(ArchitectResult), DateTimeOffset.UtcNow);

// Closed loop request (routed exclusively via run_closed_loop MCP).
[GenerateSerializer]
public record ClosedLoopRequest(string LoopType, string Prompt) : Synapse(nameof(ClosedLoopRequest), DateTimeOffset.UtcNow);

public interface IClosedLoopNeuron : INeuron, IHandle<ClosedLoopRequest>, IHandle<ExperienceUsed> { }

[GenerateSerializer]
public record InoCodeSave(string EditorId, string Code, string ExperienceName, string Description = "") : Synapse(nameof(InoCodeSave), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InoCodeExecute(string EditorId, string Code, string Instruction) : Synapse(nameof(InoCodeExecute), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InoCodeApplySkill(string EditorId, string SkillPackName) : Synapse(nameof(InoCodeApplySkill), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record SkillContextInjected(string SkillPackName, string Description, string Code) : Synapse(nameof(SkillContextInjected), DateTimeOffset.UtcNow);

public interface IInoCodeEditor : INeuron, IHandle<InoCodeEdit>, IHandle<InoCodeRun>, IHandle<InoCodeSave>, IHandle<InoCodeExecute>, IHandle<InoCodeApplySkill> { }

// Smart ContextNeuron for INO - manages chat, agent, filter, cluster contexts like context providers
[GenerateSerializer]
public record ContextUpdate(string ContextName, string Key, string Value) : Synapse(nameof(ContextUpdate), DateTimeOffset.UtcNow);

// A stored semantic memory: the text plus its embedding (empty when no real embedder is configured).
[GenerateSerializer]
public record MemoryStored(string Text, float[] Embedding) : Synapse(nameof(MemoryStored), DateTimeOffset.UtcNow);

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

public interface IObservabilityNeuron : INeuron, IHandle<UiSurface>, IHandle<ClusterActivity>, IHandle<ThreeDGraphUpdate> { }

[GenerateSerializer]
public record VisualizeDataRequest(
    string Prompt,
    string DataJson,
    string? ChartHint = null,
    string? RequestId = null,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(VisualizeDataRequest), DateTimeOffset.UtcNow, CorrelationId: RequestId);

// Company brain skill knowledge ingestion (narrow for process playbooks + transcripts).
// Used to feed raw domain knowledge into context for crystallization.
[GenerateSerializer]
public record IngestCompanySource(string Collection, string SourceId, string Text) : Synapse(nameof(IngestCompanySource), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CompanySourceIngested(string Collection, string SourceId, int ChunkCount) : Synapse(nameof(CompanySourceIngested), DateTimeOffset.UtcNow);

public interface ICompanyKnowledgeNeuron : INeuron, IHandle<IngestCompanySource> { }

[GenerateSerializer]
public record DataChartGenerated(string RequestId, UiSurface Surface) : Synapse(nameof(DataChartGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record DataChartFailed(string RequestId, string Reason) : Synapse(nameof(DataChartFailed), DateTimeOffset.UtcNow);

// First-class chart interaction and modification (conversational + selection driven)
[GenerateSerializer]
public record ChartCommand(string SurfaceId, string Instruction, string? Context = null) : Synapse(nameof(ChartCommand), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ChartInteraction(string SurfaceId, string Kind, IReadOnlyDictionary<string, object?> Payload) : Synapse(nameof(ChartInteraction), DateTimeOffset.UtcNow);

public interface IDataVisualizationNeuron : INeuron, IHandle<VisualizeDataRequest> { }

// Chart neuron supports agent metadata for routing + full conversational + selection driven updates.
public interface IChartNeuron : IAgent, IHandle<VisualizeDataRequest>, IHandle<ChartCommand>, IHandle<ChartInteraction> { }

// Closed loops for marketplace (UI authoring via Dart MCP + widget tree; SoftwareEngineering runtime mod via Aspire MCP + LLM)


[GenerateSerializer]
public record WidgetTreeInspected(string Summary, string TreeJson = "", string App = "flutter_demo") : Synapse(nameof(WidgetTreeInspected), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record UIModificationProposed(string TargetFileOrWidget, string Rationale, string ProposedDartCode) : Synapse(nameof(UIModificationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record SystemModificationProposed(string Component, string Rationale, string ProposedChange, string ApplyVia = "aspire-restart") : Synapse(nameof(SystemModificationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ClosedLoopCompleted(string LoopType, string Outcome, bool AppliedViaMcpOrMarket) : Synapse(nameof(ClosedLoopCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);
