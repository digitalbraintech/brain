namespace DigitalBrain.Protocol;

// Software-engineering "awesome" experience: real review work (not string templating).
// Harvested from final's SoftwareEngineeringTeam (ProjectReview), re-homed onto MAIN's Neuron/Synapse model.

[GenerateSerializer]
public record ReviewRequest(string Target, string DiffOrContent) : Synapse(nameof(ReviewRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ReviewProjectRequest(string Path) : Synapse(nameof(ReviewProjectRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ReviewResult(
    string Target,
    string Summary,
    string Report,
    int FileCount,
    int TodoCount,
    bool Truncated) : Synapse(nameof(ReviewResult), DateTimeOffset.UtcNow);

public interface ISoftwareEngineeringReviewer : INeuron, IHandle<ReviewRequest>, IHandle<ReviewProjectRequest> { }
