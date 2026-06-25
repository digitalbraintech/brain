using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Silo;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Silo.Gateway;

public sealed class GatewayService(
    IGrainFactory grains,
    IConfiguration configuration,
    HomeFeedBus homeFeedBus,
    ILogger<GatewayService> logger) : DigitalBrainGateway.DigitalBrainGatewayBase
{
    public override async Task<SynapseEnvelope> Send(SynapseEnvelope request, ServerCallContext context)
    {
        try
        {
            if (request.TypeName == KernelSurfaceDemo.RequestType)
            {
                await InstallAndRunSurfaceDemoAsync(request.CorrelationId);
                return request;
            }

            throw new RpcException(new Status(StatusCode.InvalidArgument, "Unsupported synapse envelope type: " + request.TypeName));
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Send failed for {TypeName}", request.TypeName);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    // Server-driven UI: stream RfwCards to the client as neurons broadcast them, until the client disconnects.
    public override async Task WatchHomeFeed(WatchHomeFeedRequest request, IServerStreamWriter<RfwCardEnvelope> responseStream, ServerCallContext context)
    {
        using var subscription = homeFeedBus.Subscribe();
        await foreach (var card in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(new RfwCardEnvelope
            {
                LibraryName = card.LibraryName,
                RootWidget = card.RootWidget,
                DataJson = card.DataJson,
                CorrelationId = card.CorrelationId ?? string.Empty,
                Timestamp = card.Timestamp.ToString("O"),
                CallerNeuronType = card.Sender?.Value ?? string.Empty
            });
        }
    }

    public override Task<HealthReply> Health(HealthRequest request, ServerCallContext context) =>
        Task.FromResult(new HealthReply
        {
            Ok = true,
            LlmMode = configuration["DigitalBrain:Llm:Provider"] ?? "none"
        });

    public override async Task<AskReply> Ask(AskRequest request, ServerCallContext context)
    {
        var neuronId = string.IsNullOrWhiteSpace(request.NeuronId) ? "ino-main" : request.NeuronId;
        if (neuronId != "ino-main")
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Ask currently supports only 'ino-main'."));

        try
        {
            var ino = grains.GetGrain<IInoNeuron>(neuronId);
            return new AskReply { Text = await ino.AskAsync(request.Prompt) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ask failed for {NeuronId}", neuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<FireReply> Fire(FireRequest request, ServerCallContext context)
    {
        try
        {
            var neuron = NeuronResolver.Resolve(grains, request.NeuronId);
            await neuron.FireAsync(new DemoMessageSynapse(request.Text));
            return new FireReply { Accepted = true };
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fire failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<TimelineReply> Timeline(TimelineRequest request, ServerCallContext context)
    {
        var max = request.MaxEntries <= 0 ? 10 : request.MaxEntries;
        INeuron neuron;
        try
        {
            neuron = NeuronResolver.Resolve(grains, request.NeuronId);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        try
        {
            var timeline = await neuron.GetTimelineAsync();
            var reply = new TimelineReply();
            foreach (var s in timeline.TakeLast(max))
            {
                reply.Entries.Add(new TimelineEntry
                {
                    Type = s.Type,
                    Timestamp = s.Timestamp.ToString("O"),
                    Text = s is DemoMessageSynapse demo ? demo.Text : (s.ToString() ?? string.Empty)
                });
            }
            return reply;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Timeline failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    private async Task InstallAndRunSurfaceDemoAsync(string correlationId)
    {
        var requestCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var pack = KernelSurfaceDemo.SignedPack();
        var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-ui-demo");
        var generated = grains.GetGrain<IGeneratedNeuron>(KernelSurfaceDemo.GeneratedNeuronKey);

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "request accepted");

        await marketplace.FireAsync(new PublishToMarketplace(
            pack.Name,
            pack.Version,
            pack.Code,
            pack.OwnerId,
            pack.IsPrivate,
            pack.CommissionRate,
            pack.Description,
            pack.AuthorPublicKeyBase64,
            pack.SignatureBase64)
        {
            CorrelationId = requestCorrelationId
        });

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "signed pack published to marketplace");

        await marketplace.FireAsync(new InstallFromMarketplace(pack.Name, pack.Version, BuyerId: "flutter-demo")
        {
            CorrelationId = requestCorrelationId
        });

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "pack installed into generated neuron");

        var demoText = string.IsNullOrWhiteSpace(correlationId)
            ? "flutter-live-demo"
            : correlationId;
        await generated.FireAsync(new DemoMessageSynapse(demoText)
        {
            CorrelationId = requestCorrelationId
        });

        var generatedTimeline = await generated.GetOutgoingTimelineAsync();
        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "journaled response and surface update observed", generatedTimeline);
    }

    private async Task PublishSurfaceDemoGraphAsync(
        string correlationId,
        string phase,
        IReadOnlyList<Synapse>? generatedTimeline = null)
    {
        var surface = KernelSurfaceDemo.ActivityGraphSurface(correlationId, phase, generatedTimeline);
        var observability = grains.GetGrain<IObservabilityNeuron>(KernelSurfaceDemo.ObservabilityNeuronKey);
        try
        {
            await observability.FireAsync(surface);
            logger.LogInformation("Published journaled surface demo graph phase={Phase} correlation={CorrelationId}", phase, correlationId);
        }
        catch (Exception ex) when (IsObservabilityJournalUnavailable(ex))
        {
            logger.LogWarning(ex, "Observability neuron unavailable; streaming graph surface without blocking phase={Phase} correlation={CorrelationId}", phase, correlationId);
            homeFeedBus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, "digitalbrain.gateway"));
        }
    }

    private static bool IsObservabilityJournalUnavailable(Exception exception) =>
        exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", StringComparison.OrdinalIgnoreCase);
}

