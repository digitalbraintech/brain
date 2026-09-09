using System.Diagnostics;
using System.Reflection;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Incoming membrane only: same-owner checks and tracing. Does not append journals or
// reinforce/bind synapses — SignalSender and Neuron remain the writers. Self-send
// never reaches this filter (in-process Deliver).
internal sealed class NeuronMembraneFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsMembraneSurface(context.InterfaceMethod))
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (!TryNeuronId(context.TargetId, out var target))
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        Authorize(context, target);

        using var activity = SignalTelemetry.Source.StartActivity("membrane");
        activity?.SetTag(SignalTelemetry.ReceiverTag, target.ToString());
        activity?.SetTag("db.method", context.MethodName);

        try
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
            throw;
        }
    }

    private static bool IsMembraneSurface(MethodInfo? method)
    {
        var declaring = method?.DeclaringType;
        return declaring == typeof(INeuronGrain)
            || declaring == typeof(INeuronQuery);
    }

    private static void Authorize(IIncomingGrainCallContext context, NeuronId target)
    {
        if (context.SourceId is { } sourceId && TryNeuronId(sourceId, out var source) && source.Owner != target.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{source}' cannot call '{target}', which belongs to owner '{target.Owner}'.");
        }

        var name = context.MethodName;
        var request = context.Request;
        if (request.GetArgumentCount() < 1)
        {
            return;
        }

        var argument = request.GetArgument(0);

        if (name == nameof(INeuronGrain.Deliver) && argument is SignalDelivery delivery && delivery.Caller.Owner != target.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{target}' refuses a delivery from foreign owner '{delivery.Caller.Owner}'.");
        }

        if ((name == nameof(INeuronGrain.BindOutgoing) || name == nameof(INeuronGrain.UnbindOutgoing)
                || name == nameof(INeuronGrain.FenceSourceEpoch) || name == nameof(INeuronGrain.FenceSourceStream))
            && argument is NeuronId subscriber
            && subscriber.Owner != target.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{target}' refuses a foreign owner '{subscriber.Owner}'.");
        }
    }

    private static bool TryNeuronId(GrainId grainId, out NeuronId id)
    {
        id = default;
        var type = grainId.Type.ToString();
        var key = grainId.Key.ToString();
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        try
        {
            id = NeuronId.FromGrainKey(type, key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
