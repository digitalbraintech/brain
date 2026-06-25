namespace DigitalBrain.Core;

[GenerateSerializer]
public record RestartResource(
    string ResourceName,
    bool IsRollingUpdate = false,
    string? TargetVersion = null,
    string? Strategy = "one-replica-at-a-time"
) : Synapse(nameof(RestartResource), DateTimeOffset.UtcNow);
