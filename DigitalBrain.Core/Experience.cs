namespace DigitalBrain.Core;

// A named, user-facing journey a domain (NeuroPack) exposes — the first-class form of the
// rows previously hand-built in UiSurfaceLiveData.ExperiencesForPack.
[GenerateSerializer]
public record Experience(
    string ExperienceId,
    string Name,
    string Kind,
    string Summary,
    IReadOnlyDictionary<string, object?> EntryAction);

// One step of an experience flow: an entry ("start") or a tap forwarded from the client.
// Args carry the selection for that hop (flightId, hotelId, ...). Mirrors ino's RfwEventRequest.
[GenerateSerializer]
public record ExperienceStep(
    string Pack,
    string ExperienceId,
    string EventName,
    IReadOnlyDictionary<string, string> Args) : Synapse(nameof(ExperienceStep), DateTimeOffset.UtcNow);
