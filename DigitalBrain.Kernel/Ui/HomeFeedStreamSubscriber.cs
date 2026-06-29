using DigitalBrain.Core;
using Orleans.Streams;

namespace DigitalBrain.Kernel.Ui;

// Per-silo stream pump: subscribes to the shared "HomeFeed" Orleans MemoryStream and fans every received card into
// this silo's local HomeFeedBus, so a card broadcast on any replica reaches clients connected to every replica.
// It hooks the silo lifecycle at the Active stage — the streaming runtime is only fully initialized by then, so
// subscribing earlier (e.g. from an IHostedService that starts before the silo) NREs inside the stream provider.
internal sealed class HomeFeedStreamSubscriber(
    HomeFeedBus bus,
    IClusterClient clusterClient,
    ILogger<HomeFeedStreamSubscriber> logger) : ILifecycleParticipant<ISiloLifecycle>
{
    private StreamSubscriptionHandle<RfwCard>? _handle;

    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(
            observerName: nameof(HomeFeedStreamSubscriber),
            stage: ServiceLifecycleStage.Active,
            onStart: async _ =>
            {
                try
                {
                    var provider = clusterClient.GetStreamProvider("HomeFeed");
                    var stream = provider.GetStream<RfwCard>(StreamId.Create("homefeed", Guid.Empty));
                    _handle = await stream.SubscribeAsync((card, _) =>
                    {
                        bus.FanLocal(card);
                        return Task.CompletedTask;
                    });
                    logger.LogInformation("HomeFeedStreamSubscriber subscribed on this silo");
                }
                catch (Exception ex)
                {
                    // Best-effort fanout: a transient stream/pub-sub failure here must not fault the Active
                    // lifecycle stage and crash the silo — degrade to no cross-silo HomeFeed on this replica.
                    logger.LogError(ex, "HomeFeedStreamSubscriber failed to subscribe; cross-silo HomeFeed fanout disabled on this silo");
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

public static class HomeFeedStreamSubscriberRegistration
{
    // Registers the per-silo HomeFeed stream pump as a silo lifecycle participant. Call inside the silo's service
    // container (UseOrleans / ISiloConfigurator) — the silo discovers it and subscribes once it reaches Active.
    public static IServiceCollection AddHomeFeedStreamSubscriber(this IServiceCollection services) =>
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, HomeFeedStreamSubscriber>();
}
