using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Gateway;

public sealed class GatewayService(
    IGrainFactory grains,
    IConfiguration configuration,
    HomeFeedBus homeFeedBus,
    IHostEnvironment environment,
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

            // Publish a pack to the marketplace. Payload carries the pack fields (and optional signature).
            // Without this, "PublishToMarketplace" fell through to the generic fallback and the pack code was
            // dropped, so nothing could later be installed/embodied.
            if (request.TypeName == nameof(PublishToMarketplace) || request.TypeName.Contains("PublishToMarketplace", StringComparison.OrdinalIgnoreCase))
            {
                var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                string Field(string key, string fallback = "") => p.TryGetValue(key, out var v) ? v?.ToString() ?? fallback : fallback;
                var packName = Field("packName", Field("name", request.CorrelationId));
                var isPrivate = bool.TryParse(Field("isPrivate"), out var priv) && priv;
                var commissionRate = double.TryParse(Field("commissionRate"), System.Globalization.CultureInfo.InvariantCulture, out var cr) ? cr : 0.10;
                var price = decimal.TryParse(Field("price"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0m;
                await market.FireAsync(new PublishToMarketplace(
                    packName, Field("version"), Field("code"), Field("ownerId", "anonymous"),
                    isPrivate, commissionRate, Field("description"),
                    Field("authorPublicKeyBase64"), Field("signatureBase64"), price));
                return request;
            }

            // Generic surface action dispatch (from UI kit RFW events / descriptors).
            // Supports install from MarketplaceList + run experiences from InstalledBundles via neurons/synapses.
            if (request.TypeName == nameof(InstallFromMarketplace) || request.TypeName.Contains("InstallFromMarketplace", StringComparison.OrdinalIgnoreCase))
            {
                var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
                // payload json carries props (packName/version/buyerId from surface action)
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                var packName = p.TryGetValue("packName", out var pn) ? pn?.ToString() ?? p.GetValueOrDefault("name")?.ToString() ?? "" : "";
                var ver = p.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
                var buyer = p.TryGetValue("buyerId", out var b)
                    ? b?.ToString()
                    : p.TryGetValue("userId", out var uid) ? uid?.ToString() : null;
                var sessionId = p.TryGetValue("sessionId", out var sid) ? sid?.ToString() : null;
                if (string.IsNullOrWhiteSpace(packName)) packName = request.CorrelationId; // fallback
                await market.FireAsync(new InstallFromMarketplace(packName, ver, string.IsNullOrWhiteSpace(buyer) ? "anonymous" : buyer, sessionId));
                return request;
            }

            if (request.TypeName == nameof(InoRequest) || request.TypeName.Contains("InoRequest", StringComparison.OrdinalIgnoreCase))
            {
                var ino = grains.GetGrain<IInoNeuron>("ino-main");
                // minimal: treat as demo for now or parse prompt; real would use props
                await ino.FireAsync(new DemoMessageSynapse("UI action: " + request.TypeName));
                return request;
            }

            if (request.TypeName == nameof(LoginRequest) || request.TypeName.Contains("LoginRequest", StringComparison.OrdinalIgnoreCase))
            {
                var session = grains.GetGrain<IUserSessionNeuron>("session-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
                var username = p.TryGetValue("username", out var u) ? u?.ToString() ?? "" : "";
                var password = p.TryGetValue("password", out var pw) ? pw?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
                await session.FireAsync(new LoginRequest(username, password, clientId));
                return request;
            }

            if (request.TypeName == nameof(LogoutRequest) || request.TypeName.Contains("LogoutRequest", StringComparison.OrdinalIgnoreCase))
            {
                var session = grains.GetGrain<IUserSessionNeuron>("session-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
                var sessionId = p.TryGetValue("sessionId", out var sid) ? sid?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
                await session.FireAsync(new LogoutRequest(sessionId, clientId));
                return request;
            }

            if (request.TypeName == nameof(ExperienceStep) || request.TypeName.Contains("ExperienceStep", StringComparison.OrdinalIgnoreCase))
            {
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(payloadStr) ?? new();
                var pack = p.GetValueOrDefault("pack", "");
                var experienceId = p.GetValueOrDefault("experienceId", "");
                var eventName = p.GetValueOrDefault("eventName", "start");
                var args = p.Where(kv => kv.Key is not ("pack" or "experienceId" or "eventName" or "synapseType"))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                var generated = grains.GetGrain<IGeneratedNeuron>("generated-" + pack.ToLowerInvariant());
                await generated.FireAsync(new ExperienceStep(pack, experienceId, eventName, args));
                return request;
            }

            // Fallback to resolver for other surface actions (experience run etc.)
            try
            {
                var neuron = NeuronResolver.Resolve(grains, request.TypeName.Contains("market", StringComparison.OrdinalIgnoreCase) ? "market-main" : "ino-main");
                await neuron.FireAsync(new DemoMessageSynapse("surface-action:" + request.TypeName));
                return request;
            }
            catch { }

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
        logger.LogInformation("WatchHomeFeed opened for {Peer}", context.Peer);
        // The first card a client sees is the login surface — pre-fill it with the dev credentials in Development.
        var initialLogin = DevAuth.Enabled(configuration, environment)
            ? UiSurfaceSamples.Login(clientId: "flutter", defaultUsername: DevAuth.Username, defaultPassword: DevAuth.Password)
            : UiSurfaceSamples.Login(clientId: "flutter");
        await WriteCardAsync(responseStream, UiSurfaceRfwBridge.FromUiSurface(initialLogin, "session-main"));
        logger.LogInformation("WatchHomeFeed sent initial login surface to {Peer}", context.Peer);

        using var subscription = homeFeedBus.Subscribe();
        await foreach (var card in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await WriteCardAsync(responseStream, card);
        }
    }

    // Surface-action payloads arrive from both Flutter (camelCase) and test/native callers (PascalCase).
    // A case-insensitive view lets one set of key lookups serve both without silent misses.
    private static Dictionary<string, object?> CaseInsensitive(Dictionary<string, object?>? source) =>
        new(source ?? new(), StringComparer.OrdinalIgnoreCase);

    private static Task WriteCardAsync(
        IServerStreamWriter<RfwCardEnvelope> responseStream,
        RfwCard card) =>
        responseStream.WriteAsync(new RfwCardEnvelope
        {
            LibraryName = card.LibraryName,
            RootWidget = card.RootWidget,
            DataJson = card.DataJson,
            CorrelationId = card.CorrelationId ?? string.Empty,
            Timestamp = card.Timestamp.ToString("O"),
            CallerNeuronType = card.Sender?.Value ?? string.Empty
        });

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

