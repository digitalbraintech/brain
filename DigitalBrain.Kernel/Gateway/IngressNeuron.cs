using DigitalBrain.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Gateway;

public interface IIngressNeuron : INeuron
{
    Task IngestAsync(string signalName, IReadOnlyDictionary<string, object?> props);
}

[GrainType("digitalbrain.ingress")]
public sealed class IngressNeuron(ILogger<IngressNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IIngressNeuron
{
    public Task IngestAsync(string signalName, IReadOnlyDictionary<string, object?> props) =>
        Broadcast(new Signal(signalName, props));
}
