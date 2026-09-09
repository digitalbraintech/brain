using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions;

public static class NeuronReferenceExtensions
{
    // The only public fire/ask: TNeuron must declare IHandle<TSignal>. That is how the
    // assistant and scripts stay type-safe: a signal can only be sent to a neuron
    // interface which explicitly declares IHandle<TSignal>.
    public static Task<DeliveryOutcome> SendAsync<TNeuron, TSignal>(
        this NeuronReference<TNeuron> neuron,
        TSignal signal,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron, IHandle<TSignal>
        where TSignal : Signal
        => neuron.DeliverAsync(signal, cancellationToken);

    // TResponse is inferred from Signal<TResponse>. C# cannot also constrain
    // TNeuron : IHandle<TRequest> on that signature (TRequest would not infer).
    // SendAsync gates at compile time; RequestAsync checks IHandle at the call.
    public static Task<TResponse> RequestAsync<TNeuron, TResponse>(
        this NeuronReference<TNeuron> neuron,
        Signal<TResponse> request,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron
        where TResponse : Signal
    {
        ArgumentNullException.ThrowIfNull(request);
        var handle = typeof(IHandle<>).MakeGenericType(request.GetType());
        if (!handle.IsAssignableFrom(typeof(TNeuron)))
        {
            throw new InvalidOperationException(
                $"Neuron '{typeof(TNeuron).Name}' does not IHandle '{request.GetType().Name}'.");
        }

        return neuron.RequestCoreAsync(request, cancellationToken);
    }

    public static Task<DeliveryOutcome> PublishAsync<TNeuron, TSignal>(
        this NeuronReference<TNeuron> neuron,
        TSignal signal,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron, IHandle<TSignal>
        where TSignal : Signal
        => neuron.SendAsync(signal, cancellationToken);

    public static Task SubscribeToAsync<TSelf, TSource, TSignal>(
        this NeuronReference<TSelf> subscriber,
        NeuronId source,
        CancellationToken cancellationToken = default)
        where TSelf : INeuron, IHandle<TSignal>
        where TSource : INeuron
        where TSignal : Signal
        => subscriber.SubscribeToAsync<TSelf, TSource, TSignal>(source, correlation: null, cancellationToken);

    public static Task SubscribeToAsync<TSelf, TSource, TSignal>(
        this NeuronReference<TSelf> subscriber,
        NeuronId source,
        CorrelationId? correlation,
        CancellationToken cancellationToken = default)
        where TSelf : INeuron, IHandle<TSignal>
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

    public static Task UnsubscribeFromAsync<TSelf, TSource, TSignal>(
        this NeuronReference<TSelf> subscriber,
        NeuronId source,
        CancellationToken cancellationToken = default)
        where TSelf : INeuron, IHandle<TSignal>
        where TSource : INeuron
        where TSignal : Signal
        => subscriber.UnsubscribeFromAsync<TSelf, TSource, TSignal>(source, correlation: null, cancellationToken);

    public static Task UnsubscribeFromAsync<TSelf, TSource, TSignal>(
        this NeuronReference<TSelf> subscriber,
        NeuronId source,
        CorrelationId? correlation,
        CancellationToken cancellationToken = default)
        where TSelf : INeuron, IHandle<TSignal>
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

        return subscriber.SendAsync(new Unsubscribe(source, typeof(TSignal).Name, correlation), cancellationToken);
    }
}
