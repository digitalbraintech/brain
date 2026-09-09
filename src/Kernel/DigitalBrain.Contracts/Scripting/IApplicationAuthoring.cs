namespace DigitalBrain.Abstractions.Scripting;

public interface IApplicationAuthoring
{
    Task<ApplicationCapabilityCatalog> CatalogAsync(IDigitalBrain brain,
        CancellationToken cancellationToken = default);
    Task<ApplicationExpectations> SetExpectationsAsync(IDigitalBrain brain, string key, string documentJson,
        Guid operationId, string? expectedExpectationRevision, CancellationToken cancellationToken = default);
    Task<ApplicationExpectations> ReadExpectationsAsync(IDigitalBrain brain, string key,
        string expectationRevision, CancellationToken cancellationToken = default);
    Task<ApplicationScenarioRun> RunScenariosAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default);
    Task<ApplicationScenarioRun> ReadScenarioRunAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default);
    Task<string> TemplateAsync(IDigitalBrain brain, string key, CancellationToken cancellationToken = default);
    Task<ApplicationInvocation> ReadInvocationAsync(IDigitalBrain brain, string key,
        Guid operationId, CancellationToken cancellationToken = default);
    Task<ApplicationInvocation> CancelInvocationAsync(IDigitalBrain brain, string key,
        Guid operationId, CancellationToken cancellationToken = default);
    Task<ApplicationInvocation> InvokeAsync(IDigitalBrain brain, string key, string operation,
        string inputJson, Guid operationId, CancellationToken cancellationToken = default);
    Task<ApplicationSource> SaveAsync(IDigitalBrain brain, string key, string source,
        string? expectedRevision, CancellationToken cancellationToken = default);
    Task<ApplicationSource> ReadAsync(IDigitalBrain brain, string key,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListFilesAsync(IDigitalBrain brain, string key,
        CancellationToken cancellationToken = default);
    Task<ApplicationSource> ReadFileAsync(IDigitalBrain brain, string key, string path,
        CancellationToken cancellationToken = default);
    Task<ApplicationSource> SaveFileAsync(IDigitalBrain brain, string key, string path, string source,
        string expectedRevision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationSourceSummary>> ListAsync(IDigitalBrain brain,
        CancellationToken cancellationToken = default);
    Task<ApplicationValidation> ValidateAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default);
    Task<ApplicationDescription> DescribeAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default);
    Task<ApplicationActivation> ActivateAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default);
}

public sealed record ApplicationExpectations(
    string Key,
    string ExpectationRevision,
    string DocumentJson,
    Guid OperationId,
    Guid PrincipalId,
    DateTimeOffset RecordedAt);

public sealed record ApplicationCapabilityCatalog(IReadOnlyList<ApplicationNeuronCapability> Neurons);

public sealed record ApplicationNeuronCapability(
    string Key,
    string Contract,
    string DefaultInstanceName,
    string ProjectReference,
    IReadOnlyList<ApplicationNeuronInputCapability> Inputs,
    IReadOnlyList<ApplicationNeuronEventCapability> Events);

public sealed record ApplicationNeuronInputCapability(
    string Key,
    ApplicationJsonContract Request,
    ApplicationJsonContract? Response);

public sealed record ApplicationNeuronEventCapability(
    string Key,
    ApplicationJsonContract Payload);

public sealed record ApplicationSource(string Key, string SourceRevision, string Source,
    string? ActiveRevision = null, string? PendingActivationRevision = null,
    string Path = "application.cs");

public sealed record ApplicationSourceSummary(string Key, string SourceRevision, bool Validated,
    string? ActiveRevision, string? PendingActivationRevision);

public sealed record ApplicationValidation(string SourceRevision, bool Succeeded, string? Diagnostics);

public sealed record ApplicationDescription(
    string Key,
    string SourceRevision,
    string ApplicationRevision,
    IReadOnlyList<ApplicationOperationDescription> Operations);

public sealed record ApplicationOperationDescription(
    string Key,
    string Summary,
    ApplicationJsonContract Request,
    ApplicationJsonContract Response,
    bool IsQuery);

public sealed record ApplicationJsonContract(
    string Name,
    string? JsonSchema,
    string? ExampleJson,
    string? TypeName = null);

public sealed record ApplicationActivation(string Key, string SourceRevision, string ArtifactRevision);

[GenerateSerializer, Alias("db.application-invocation")]
public sealed record ApplicationInvocation(
    [property: Id(0)] Guid OperationId, [property: Id(1)] string Revision,
    [property: Id(2)] string Operation, [property: Id(3)] string Status,
    [property: Id(4)] string? Value, [property: Id(5)] string? Error);
