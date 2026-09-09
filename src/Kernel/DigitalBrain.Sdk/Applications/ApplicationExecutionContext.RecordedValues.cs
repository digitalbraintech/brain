namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationExecutionContext
{
    public Task<DateTimeOffset> UtcNowAsync(CancellationToken cancellationToken = default)
        => CheckpointAsync(
            "system.utc-now/v1",
            revision: null,
            static (_, _) => Task.FromResult(TimeProvider.System.GetUtcNow()),
            cancellationToken);

    public Task<Guid> NewGuidAsync(CancellationToken cancellationToken = default)
        => CheckpointAsync(
            "system.new-guid/v1",
            revision: null,
            static (_, _) => Task.FromResult(Guid.NewGuid()),
            cancellationToken);
}
