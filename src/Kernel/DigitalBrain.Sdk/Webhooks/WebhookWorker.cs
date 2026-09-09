using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Sdk.Webhooks;
/// <summary>Wake hints only. Every work item and retry remains in its source neuron.</summary>
public sealed class WebhookWakeups
{
    internal readonly Channel<(NeuronId Source, ActorContext Actor)> Channel = System.Threading.Channels.Channel.CreateBounded<(NeuronId, ActorContext)>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    public void Wake(NeuronId source, ActorContext actor) => Channel.Writer.TryWrite((source, actor));
}

public static class WebhookHostingExtensions
{
    public static IServiceCollection AddWebhookNeurons(this IServiceCollection services)
    {
        services.TryAddSingleton<WebhookWakeups>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebhookWorker>());
        return services;
    }
}

internal sealed class WebhookWorker(IGrainFactory grains, WebhookWakeups wakeups, IEnumerable<IWebhookProcessor> processors, IHostApplicationLifetime lifetime, ILogger<WebhookWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<NeuronId, Channel<bool>> _running = new();
    private readonly SemaphoreSlim _ioCapacity = new(32);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => ready.TrySetResult());
        await ready.Task.WaitAsync(stoppingToken);
        await foreach (var work in wakeups.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            var hints = System.Threading.Channels.Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
            if (_running.TryAdd(work.Source, hints))
            {
                _ = DrainAsync(work.Source, work.Actor, hints, stoppingToken);
            }
            else if (!_running.TryGetValue(work.Source, out var active) || !active.Writer.TryWrite(true))
            {
                wakeups.Wake(work.Source, work.Actor);
            }
        }
    }

    private async Task DrainAsync(NeuronId source, ActorContext actor, Channel<bool> hints, CancellationToken token)
    {
        try
        {
            using var verified = VerifiedActor.Enter(actor);
            var endpoint = grains.GetGrain<IAuthenticatedWebhookIngress>(source.ToGrainId());
            var inFlight = new List<Task>();
            Task? receiptProcessing = null;
            Task<bool>? nextWake = null;
            while (!token.IsCancellationRequested)
            {
                while (hints.Reader.TryRead(out _)) { }
                if (nextWake is null || nextWake.IsCompleted)
                {
                    nextWake = hints.Reader.WaitToReadAsync(token).AsTask();
                }
                inFlight.RemoveAll(task => task.IsCompleted);
                if (inFlight.Count >= 8)
                {
                    await Task.WhenAny(inFlight).WaitAsync(token);
                    continue;
                }
                var work = await endpoint.ClaimAsync().WaitAsync(token);
                if (work is null)
                {
                    if (inFlight.Count == 0)
                    {
                        return;
                    }
                    await Task.WhenAny(inFlight.Append(nextWake)).WaitAsync(token);
                    continue;
                }
                if (work.Receipt is not null && receiptProcessing is { IsCompleted: false })
                {
                    // Provider observations commit in source order; delivery to unrelated
                    // recipients progresses while that I/O is running.
                    await receiptProcessing.WaitAsync(token);
                }
                var processing = ProcessAsync(source, endpoint, work, token);
                inFlight.Add(processing);
                if (work.Receipt is not null)
                {
                    receiptProcessing = processing;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            logger.LogWarning("Webhook recovery on {Source} deferred after {FailureType}.", source, error.GetType().Name);
        }
        finally
        {
            hints.Writer.TryComplete();
            _running.TryRemove(source, out _);
            if (hints.Reader.TryRead(out _) && !token.IsCancellationRequested)
            {
                wakeups.Wake(source, actor);
            }
        }
    }

    private async Task ProcessAsync(NeuronId source, IAuthenticatedWebhookIngress endpoint, WebhookWork work, CancellationToken token)
    {
        var acquired = false;
        try
        {
            await _ioCapacity.WaitAsync(token);
            acquired = true;
            if (work.Receipt is { } receipt)
            {
                using var trace = WebhookTrace.Start("webhook.process", receipt);
                var processor = processors.SingleOrDefault(item => item.Handles(source));
                Signal[] results = processor is null ? [receipt.Input] : await processor.ProcessAsync(source, receipt, token);
                await endpoint.CompleteAsync(work.Lease, results).WaitAsync(token);
            }
            else if (work.FenceEpoch is { } epoch && work.Recipient is { } fencedRecipient)
            {
                await grains.GetGrain<INeuronGrain>(fencedRecipient.ToGrainId()).FenceSourceEpoch(source, epoch)
                    .WaitAsync(TimeSpan.FromSeconds(20), token);
                await endpoint.AcknowledgeAsync(work.Lease, true).WaitAsync(token);
            }
            else if (work.Delivery is { } delivery && work.Recipient is { } recipient)
            {
                // Independent bounded recipient attempts. No source turn or Learned-route mutation.
                var result = await grains.GetGrain<INeuronGrain>(recipient.ToGrainId()).Deliver(delivery, token)
                    .WaitAsync(TimeSpan.FromSeconds(20), token);
                await endpoint.AcknowledgeAsync(work.Lease, work.IsReply || result == DeliveryOutcome.Handled).WaitAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            try { await endpoint.FailAsync(work.Lease).WaitAsync(token); }
            catch (Exception) { /* The durable lease will expire and recover. */ }
            logger.LogWarning("Webhook work on {Source} will retry after {FailureType}.", source, error.GetType().Name);
        }
        finally
        {
            if (acquired)
            {
                _ioCapacity.Release();
            }
        }
    }
}
