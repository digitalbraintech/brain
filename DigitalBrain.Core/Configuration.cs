namespace DigitalBrain.Core;

// Fired by the host (or UI layer) once the user has filled in all RequiredConfig fields for a pack.
// Secret values in Values must never be logged.
[GenerateSerializer]
public record ConfigurationProvided(
    [property: Id(0)] string PackName,
    [property: Id(1)] IReadOnlyDictionary<string, string> Values)
    : Synapse(nameof(ConfigurationProvided), DateTimeOffset.UtcNow);
