using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.AI;

public static class AssistantReferenceExtensions
{
    public static Task SubscribeToAsync<TSource, TSignal>(
        this NeuronReference<IAssistant> subscriber,
        NeuronId source,
        CancellationToken cancellationToken = default)
        where TSource : INeuron
        where TSignal : Signal
        => subscriber.SubscribeToAsync<TSource, TSignal>(source, correlation: null, cancellationToken);

    public static Task SubscribeToAsync<TSource, TSignal>(
        this NeuronReference<IAssistant> subscriber,
        NeuronId source,
        CorrelationId? correlation,
        CancellationToken cancellationToken = default)
        where TSource : INeuron
        where TSignal : Signal
    {
        var expected = NeuronId.For<TSource>(source.Owner, source.Name);
        if (source != expected)
        {
            throw new ArgumentException(
                $"Neuron '{source}' is not a '{expected.Type}' instance.",
                nameof(source));
        }

        return subscriber.SendAsync(new Subscribe(source, typeof(TSignal).Name, correlation), cancellationToken);
    }
}
