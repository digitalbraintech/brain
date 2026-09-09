using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(IWorkspaceIndex.GrainTypeName)]
internal sealed class WorkspaceIndexEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WorkspaceIndexState> state)
    : Entity<WorkspaceIndexState>(state), IWorkspaceIndex
{
    public async Task Ensure(string name, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var current = State?.Workspaces ?? [];
        var existing = current.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            var updated = existing with { Title = title.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
            await SaveAsync(new WorkspaceIndexState(
                [.. current.Select(item => item.Name == name ? updated : item)]));
            return;
        }

        var record = new WorkspaceRecord(
            name.Trim(),
            Guid.NewGuid().ToString("n"),
            title.Trim(),
            DateTimeOffset.UtcNow);
        await SaveAsync(new WorkspaceIndexState([.. current, record]));
    }

    public Task<WorkspaceRecord?> Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Task.FromResult(
            State?.Workspaces.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal)));
    }

    public Task<WorkspaceRecord?> FindByCorrelation(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return Task.FromResult(
            State?.Workspaces.FirstOrDefault(item =>
                string.Equals(item.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase)));
    }
}
