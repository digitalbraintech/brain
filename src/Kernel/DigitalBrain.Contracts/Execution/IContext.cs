using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Execution;

public interface IContext : IAsyncDisposable
{
    ExecutionId ExecutionId { get; }
    ExecutionIdentity Identity { get; }
    CancellationToken CancellationToken { get; }
    T Input<T>() where T : Signal;
    NeuronReference<T> Get<T>(string name) where T : INeuron;
    Task<TReply> RequestAsync<TTarget, TReply>(
        string step,
        NeuronReference<TTarget> target,
        Signal<TReply> request,
        CancellationToken cancellationToken = default)
        where TTarget : INeuron
        where TReply : Signal;
    Task SendAsync<TTarget>(
        string step,
        NeuronReference<TTarget> target,
        Signal command,
        CancellationToken cancellationToken = default)
        where TTarget : INeuron;
    Task PublishAsync(string step, Signal message, CancellationToken cancellationToken = default);
    Task CompleteAsync(Signal response);
    Task CompleteAsync();
}
