using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.AI;

// A local, turn-scoped capability. Never put this in serializable RequestContext.
public sealed class AgentToolContext(
    NeuronId agent,
    PrincipalId? principal,
    IAgentRequests requests,
    Func<AgentActivity, Task>? recordActivity = null) : IDisposable
{
    private bool _disposed;

    public NeuronId Agent { get; } = agent;
    public PrincipalId? Principal { get; } = principal;
    public IAgentRequests Requests { get; } = requests;

    public OwnerId Owner => Agent.Owner;

    public void RequireActive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public Task ObserveAsync(AgentActivity activity)
    {
        RequireActive();
        return recordActivity?.Invoke(activity) ?? Task.CompletedTask;
    }

    public void Dispose() => _disposed = true;
}

public interface IAgentRequests
{
    Task<DeliveryOutcome> SendAsync(NeuronId target, Signal signal, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This context does not support neuron composition.");

    Task<TResponse> RequestAsync<TResponse>(NeuronId target, Signal<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : Signal
        => throw new NotSupportedException("This context does not support neuron composition.");

    Task<AgentReply> RequestAsync<TAgent>(
        string instanceName,
        AgentRequest request,
        CancellationToken cancellationToken = default)
        where TAgent : IAgent;
}
