using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;

namespace DigitalBrain.Core;

[GrainType(IActivitySource.GrainTypeName)]
internal sealed class ActivitySourceNeuron : Neuron, IActivitySource
{
    private readonly IDurableDictionary<long, byte[]> _pending;
    private readonly IDurableValue<long> _sequence;
    private readonly Serializer<SignalDelivery> _serializer;
    private readonly ILogger<ActivitySourceNeuron> _logger;

    public ActivitySourceNeuron(NeuronRuntime runtime) : base(runtime)
    {
        _pending = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<long, byte[]>>("activities.pending.v1");
        _sequence = ServiceProvider.GetRequiredKeyedService<IDurableValue<long>>("activities.pending.sequence.v1");
        _serializer = ServiceProvider.GetRequiredService<Serializer<SignalDelivery>>();
        _logger = ServiceProvider.GetRequiredService<ILogger<ActivitySourceNeuron>>();
    }

    protected override Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        // Only this drain may interleave: a producer can report its next fact while
        // an observer waits for that producer's current handler to return. The drain
        // uses captured envelopes, never the mutable CurrentDelivery of another turn.
        _ = this.RegisterGrainTimer(DrainAsync, new GrainTimerCreationOptions
        {
            DueTime = TimeSpan.FromMilliseconds(100),
            Period = TimeSpan.FromMilliseconds(100),
            Interleave = true,
            KeepAlive = true,
        });
        return Task.CompletedTask;
    }

    public async Task HandleAsync(ActivityExecutionChanged signal, CancellationToken cancellationToken)
    {
        var delivery = CurrentDelivery ?? throw new InvalidOperationException("Activity facts require a delivery envelope.");
        ActivityFacts.RequireOwner(signal, Id.Owner, delivery.Principal);
        var sequence = checked(_sequence.Value + 1);
        _sequence.Value = sequence;
        _pending[sequence] = _serializer.SerializeToArray(delivery);
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var path = NeuronRequestPath.Clear();
        // Preserve facts produced before startup wires the activity collector.
        if (!(await ReadSynapses().ConfigureAwait(true)).Any(edge => edge.SignalType == nameof(ActivityExecutionChanged)))
        {
            return;
        }

        // Snapshot before any await. Input turns only append new keys; successful
        // dispatch removes its own key without replacing concurrently appended state.
        foreach (var pending in _pending.OrderBy(pair => pair.Key).Take(32).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delivery = _serializer.Deserialize(pending.Value);
            using var actor = VerifiedActor.Enter(delivery.Principal is { } principal
                ? new ActorContext(principal, "_activity") : null);
            try
            {
                if (await BroadcastAsync(delivery.Signal, delivery).ConfigureAwait(true) == 0)
                {
                    return;
                }
                _pending.Remove(pending.Key);
                await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Activity delivery {Sequence} will be retried.", pending.Key);
                // Retain order and leave the durable envelope available after restart.
                return;
            }
        }
    }
}
