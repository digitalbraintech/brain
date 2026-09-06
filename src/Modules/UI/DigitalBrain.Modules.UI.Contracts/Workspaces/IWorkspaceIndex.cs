using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.UI;

[Alias("ui.workspace-index")]
public interface IWorkspaceIndex : IEntity<WorkspaceIndexState>
{
    const string GrainTypeName = "workspace-index";
    const string DefaultInstanceName = "default";

    [Alias(nameof(Ensure))]
    Task Ensure(string name, string title);

    [Alias(nameof(Find))]
    Task<WorkspaceRecord?> Find(string name);

    [Alias(nameof(FindByCorrelation))]
    Task<WorkspaceRecord?> FindByCorrelation(string correlationId);
}

[GenerateSerializer]
[Alias("ui.workspace-index-state")]
public sealed record WorkspaceIndexState(
    [property: Id(0)] IReadOnlyList<WorkspaceRecord> Workspaces);

[GenerateSerializer]
[Alias("ui.workspace-record")]
public sealed record WorkspaceRecord(
    [property: Id(0)] string Name,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] string Title,
    [property: Id(3)] DateTimeOffset UpdatedAt);

[Alias("ui.workspace-inject")]
public interface IWorkspaceInject
{
    Task<CommandId> InjectUserMessage(
        string workspaceName,
        string text,
        ActorContext actor,
        CancellationToken cancellationToken = default,
        CommandId? commandId = null);
}
