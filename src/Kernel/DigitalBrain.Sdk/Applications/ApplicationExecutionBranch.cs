using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Scripting;

public sealed class ApplicationExecutionBranch
{
    private readonly ApplicationExecutionContext context;
    private readonly string name;
    private int called;

    internal ApplicationExecutionBranch(ApplicationExecutionContext context, string name)
        => (this.context, this.name) = (context, name);

    public Task<TResult> InvokeAsync<TRequest, TResult>(CommandPort<TRequest, TResult> target,
        TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ReserveEffect();
        return context.InvokeBranchAsync(name, target, request, cancellationToken);
    }

    public Task<TResponse> CallAsync<TNeuron, TResponse>(NeuronReference<TNeuron> target,
        Signal<TResponse> request, CancellationToken cancellationToken = default)
        where TNeuron : INeuron
        where TResponse : Signal
    {
        ArgumentNullException.ThrowIfNull(request);
        ReserveEffect();
        var handled = typeof(IHandle<>).MakeGenericType(request.GetType());
        if (!handled.IsAssignableFrom(typeof(TNeuron)))
        {
            throw new InvalidOperationException(
                $"Neuron '{typeof(TNeuron).Name}' does not IHandle '{request.GetType().Name}'.");
        }
        return context.CallBranchAsync(name, target, request, cancellationToken);
    }

    private void ReserveEffect()
    {
        if (Interlocked.Exchange(ref called, 1) != 0)
        {
            throw new InvalidOperationException($"Parallel branch '{name}' supports one recorded effect.");
        }
    }
}
