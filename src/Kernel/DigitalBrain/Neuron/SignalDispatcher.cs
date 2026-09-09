using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class SignalDispatcher
{
    private delegate Task HandlerInvoker(
        object neuron,
        Signal signal,
        CancellationToken cancellationToken);

    private readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, HandlerInvoker>> _handlersByNeuronType = new();

    internal async Task<DeliveryOutcome> DispatchAsync(
        object neuron,
        Signal signal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentNullException.ThrowIfNull(signal);

        if (!HandlersFor(neuron.GetType()).TryGetValue(signal.GetType(), out var handler))
        {
            return DeliveryOutcome.Unhandled;
        }

        await handler(neuron, signal, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return DeliveryOutcome.Handled;
    }

    private IReadOnlyDictionary<Type, HandlerInvoker> HandlersFor(Type neuronType)
        => _handlersByNeuronType.GetOrAdd(neuronType, static type => BuildHandlers(type));

    private static Dictionary<Type, HandlerInvoker> BuildHandlers(Type neuronType)
    {
        var handlers = new Dictionary<Type, HandlerInvoker>();

        foreach (var handled in neuronType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IHandle<>)))
        {
            var signalType = handled.GetGenericArguments()[0];
            var handleMethod = handled.GetMethod(nameof(IHandle<>.HandleAsync))
                ?? throw new MissingMethodException(handled.FullName, nameof(IHandle<>.HandleAsync));

            handlers[signalType] = (neuron, signal, cancellationToken) => (Task)handleMethod.Invoke(
                neuron,
                BindingFlags.DoNotWrapExceptions,
                binder: null,
                [signal, cancellationToken],
                culture: null)!;
        }

        return handlers;
    }
}
