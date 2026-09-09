namespace DigitalBrain.Product.Presentation;

/// <summary>
/// Module-owned presentation for an already-observed neuron type. This descriptor
/// does not register instances, capabilities, subscriptions, or graph connections.
/// Icon keys identify bundled UI assets; they are never asset paths or URLs.
/// </summary>
public sealed record NeuronPresentation(string NeuronType, string Label, string Module, string IconKey);
