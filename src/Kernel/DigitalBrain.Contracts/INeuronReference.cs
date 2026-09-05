using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions;

public interface INeuronReference
{
    NeuronId Id { get; }
    Task<IReadOnlyList<string>> PublishedSignalTypesAsync(CancellationToken cancellationToken = default);
}
