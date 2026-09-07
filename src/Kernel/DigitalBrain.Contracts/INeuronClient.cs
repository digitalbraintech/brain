using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

// Keeps typed neuron references independent of hosting and the SDK implementation.
internal interface INeuronClient
{
    OwnerId Owner { get; }
    PrincipalId? Principal { get; }
    Task<DeliveryOutcome> SendAsync(NeuronId receiver, Signal signal, CancellationToken cancellationToken);
    Task<TResponse> SendRequestAsync<TResponse>(NeuronId receiver, Signal request, CancellationToken cancellationToken)
        where TResponse : Signal;
    Task<JournalRead> ReadJournalAsync(NeuronId subject, JournalKind kind, long afterSequence, CancellationToken cancellationToken);
    IAsyncEnumerable<JournalRead> WatchJournalAsync(NeuronId subject, JournalKind kind, long afterSequence, CancellationToken cancellationToken);
    Task<IReadOnlyList<Synapse>> GetSynapsesAsync(NeuronId subject, CancellationToken cancellationToken);
}
