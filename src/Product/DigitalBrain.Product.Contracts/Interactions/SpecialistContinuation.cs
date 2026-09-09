using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Product.Interactions;

// Trusted control data from the initiating neuron, never a model-selected address.
[GenerateSerializer]
[Alias("db.specialist-request")]
public sealed record SpecialistRequest(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] string Text);

// Credentials stay in the connection store. Revision is an opaque binding identity.
[GenerateSerializer]
[Alias("db.specialist-continuation")]
public sealed record SpecialistContinuation(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] string RequestText,
    [property: Id(2)] string[] AllowedToolNames,
    [property: Id(3)] string? ConnectionRevision = null);
