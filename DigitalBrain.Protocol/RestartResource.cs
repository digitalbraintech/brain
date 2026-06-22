namespace DigitalBrain.Protocol;

[GenerateSerializer]
public record RestartResource(string ResourceName) : Synapse(nameof(RestartResource), DateTimeOffset.UtcNow);
