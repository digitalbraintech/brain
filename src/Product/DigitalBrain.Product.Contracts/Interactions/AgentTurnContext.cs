using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Product.Interactions;

[GenerateSerializer]
[Alias("db.agent-turn-context")]
public sealed record AgentTurnContext(
    [property: Id(0)] NeuronId Chat,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] ActorContext Actor,
    [property: Id(3)] string[]? AllowedToolNames = null,
    [property: Id(4)] SpecialistRequest? SpecialistRequest = null,
    [property: Id(5)] SpecialistContinuation? SpecialistContinuation = null,
    [property: Id(6)] SetupContinuation? SetupContinuation = null)
{
    private const string ContextKey = "db.agent-turn-context";

    public static AgentTurnContext? Current => RequestContext.Get(ContextKey) as AgentTurnContext;

    public static IDisposable Enter(AgentTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = RequestContext.Get(ContextKey);
        RequestContext.Set(ContextKey, context);
        return new Restore(previous);
    }

    private sealed class Restore(object? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (previous is null)
            {
                RequestContext.Remove(ContextKey);
            }
            else
            {
                RequestContext.Set(ContextKey, previous);
            }
        }
    }
}
