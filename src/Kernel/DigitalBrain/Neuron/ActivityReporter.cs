using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class ActivityReporter(NeuronId neuron, IGrainFactory grains, TimeProvider clock)
{
    internal static bool Tracks(SignalDelivery delivery)
        => !delivery.IsActivityTelemetry
            && delivery.Signal is not (IActivityTelemetry or Subscribe or Unsubscribe or DigitalBrainActivated);

    internal async Task ReportAsync(
        SignalDelivery delivery, string operationId, string phase, NeuronId? target, string? detail = null)
    {
        if (!Tracks(delivery) || neuron.Type is IActivities.GrainTypeName or IActivitySource.GrainTypeName)
        {
            return;
        }

        var payload = delivery.Signal;
        // Optional product metadata does not make the kernel depend on product signal assemblies.
        var title = payload.GetType().GetProperty("Text")?.GetValue(payload) as string;
        var command = payload.GetType().GetProperty("CommandId")?.GetValue(payload)?.ToString();
        var fact = new ActivityExecutionChanged(delivery.CorrelationId, delivery.Principal, operationId,
            delivery.SignalId, delivery.CausationId, delivery.Caller, target, payload.GetType().Name,
            phase, clock.GetUtcNow(), title is { Length: > 160 } ? title[..160] : title, command, detail);
        var source = NeuronId.For<IActivitySource>(neuron.Owner, IActivitySource.DefaultInstanceName);
        using var path = NeuronRequestPath.Enter(neuron, source);
        await grains.GetGrain<INeuronGrain>(source.ToGrainId())
            .Deliver(SignalDelivery.Create(fact, neuron, 1, clock, principal: delivery.Principal), CancellationToken.None)
            .ConfigureAwait(true);
    }
}
