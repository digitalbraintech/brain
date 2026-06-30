using DigitalBrain.Core;
using Orleans.Streams;

namespace DigitalBrain.Kernel.Ui;

// Per-silo stream pump: subscribes to the shared DigitalBrainTimeline stream (which carries ALL broadcast synapses)
// and forwards only Signals into this silo's SignalEgressBus, so a Signal broadcast on any replica reaches every
// replica's WatchSynapses subscribers. Hooks the silo lifecycle at the Active stage — the streaming runtime is only
// fully initialized by then, so subscribing earlier NREs inside the stream provider.
internal sealed class SignalEgressStreamSubscriber(
    SignalEgressBus bus,
    IClusterClient clusterClient,
    ILogger<SignalEgressStreamSubscriber> logger) : ILifecycleParticipant<ISiloLifecycle>
{
    private StreamSubscriptionHandle<Synapse>? _handle;

    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(
            observerName: nameof(SignalEgressStreamSubscriber),
            stage: ServiceLifecycleStage.Active,
            onStart: async _ =>
            {
                try
                {
                    var stream = clusterClient.GetStreamProvider(SynapseStream.ProviderName).Timeline();
                    _handle = await stream.SubscribeAsync((synapse, _) =>
                    {
                        if (synapse is Signal signal)
                            bus.Publish(signal);
                        return Task.CompletedTask;
                    });
                    logger.LogInformation("SignalEgressStreamSubscriber subscribed on this silo");
                }
                catch (Exception ex)
                {
                    // Best-effort egress: a transient stream/pub-sub failure here must not fault the Active lifecycle
                    // stage and crash the silo — degrade to no Signal egress on this replica.
                    logger.LogError(ex, "SignalEgressStreamSubscriber failed to subscribe; Signal egress disabled on this silo");
                }
            },
            onStop: async _ =>
            {
                if (_handle is not null)
                {
                    await _handle.UnsubscribeAsync();
                    _handle = null;
                }
            });
}

public static class SignalEgressStreamSubscriberRegistration
{
    // Registers the per-silo Signal egress stream pump as a silo lifecycle participant. Call inside the silo's
    // service container (UseOrleans / ISiloConfigurator) — the silo discovers it and subscribes once it reaches Active.
    public static IServiceCollection AddSignalEgressStreamSubscriber(this IServiceCollection services) =>
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, SignalEgressStreamSubscriber>();
}
