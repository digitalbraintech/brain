using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions;

public sealed class Context : IContext
{
    private static readonly AsyncLocal<IContext?> Bound = new();

    private Context()
    {
    }

    public static IContext Current
        => Bound.Value
            ?? throw new InvalidOperationException("No execution context is bound.");

    public static Task<IContext> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<IContext>(
            new NotSupportedException("Process bootstrap is not part of the in-process communication slice."));
    }

    public ExecutionId ExecutionId => ThrowUnbound<ExecutionId>();
    public ExecutionIdentity Identity => ThrowUnbound<ExecutionIdentity>();
    public CancellationToken CancellationToken => ThrowUnbound<CancellationToken>();

    public T Input<T>() where T : Signal => ThrowUnbound<T>();

    public NeuronReference<T> Get<T>(string name) where T : INeuron
        => ThrowUnbound<NeuronReference<T>>();

    public Task<TReply> RequestAsync<TTarget, TReply>(
        string step,
        NeuronReference<TTarget> target,
        Signal<TReply> request,
        CancellationToken cancellationToken = default)
        where TTarget : INeuron
        where TReply : Signal
        => Task.FromException<TReply>(Unbound());

    public Task SendAsync<TTarget>(
        string step,
        NeuronReference<TTarget> target,
        Signal command,
        CancellationToken cancellationToken = default)
        where TTarget : INeuron
        => Task.FromException(Unbound());

    public Task PublishAsync(string step, Signal message, CancellationToken cancellationToken = default)
        => Task.FromException(Unbound());

    public Task CompleteAsync(Signal response) => Task.FromException(Unbound());

    public Task CompleteAsync() => Task.FromException(Unbound());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static IDisposable Bind(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = Bound.Value;
        Bound.Value = context;
        return new Restore(previous);
    }

    private static T ThrowUnbound<T>() => throw Unbound();

    private static InvalidOperationException Unbound()
        => new("No execution context is bound.");

    private sealed class Restore(IContext? previous) : IDisposable
    {
        public void Dispose() => Bound.Value = previous;
    }
}
