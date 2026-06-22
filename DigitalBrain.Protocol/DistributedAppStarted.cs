namespace DigitalBrain.Protocol;

[GenerateSerializer]
public record DistributedAppStarted(string AppName, bool Success, string? Details = null) : Synapse(nameof(DistributedAppStarted), DateTimeOffset.UtcNow);
