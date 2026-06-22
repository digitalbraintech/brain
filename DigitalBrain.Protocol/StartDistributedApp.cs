namespace DigitalBrain.Protocol;

// Command / event synapses for core system neurons (per v2 spec)
[GenerateSerializer]
public record StartDistributedApp(string AppName) : Synapse(nameof(StartDistributedApp), DateTimeOffset.UtcNow);
