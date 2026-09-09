using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// This is a transient awaited delivery/binding chain carried by Orleans, never a graph registry.
// Strings and arrays use Orleans' built-in serializers and cannot retain an
// executable delegate or an activation after the turn ends.
internal static class NeuronRequestPath
{
    private const string ContextKey = "DigitalBrain.Neuron.RequestPath";

    internal static IDisposable Enter(NeuronId source, NeuronId receiver)
    {
        var previous = RequestContext.Get(ContextKey);
        var path = previous as string[] ?? [];
        if (source == receiver)
        {
            // Self-delivery and binding run locally inside the current activation.
            return new Restore(previous);
        }

        if (path.Contains(receiver.ToString(), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Awaited call to neuron '{receiver}' would create a cycle in the active request path.");
        }

        RequestContext.Set(ContextKey, path.Append(source.ToString()).ToArray());
        return new Restore(previous);
    }

    internal static IDisposable Clear()
    {
        var previous = RequestContext.Get(ContextKey);
        RequestContext.Remove(ContextKey);
        return new Restore(previous);
    }

    private sealed class Restore(object? previous) : IDisposable
    {
        public void Dispose()
        {
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
