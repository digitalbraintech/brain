using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc.Ui;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Gateway;

// Bidirectional UI session handler. Complements WatchHomeFeed (one-way RfwCard stream).
// Supports interactive canvas (DRAG, CLICK etc from RFW onEvent) and kernel signals back (viewport, future rfw/sound).
// Inputs from kit nodes (neuron:ActionButton etc) now dispatch to real typed synapses via action descriptors in payload.
public sealed class UiGatewayService(
    IGrainFactory grainFactory,
    ILogger<UiGatewayService> logger) : UiGateway.UiGatewayBase
{
    public override async Task EngageUiSession(
        IAsyncStreamReader<UiInputSynapse> requestStream,
        IServerStreamWriter<UiStateSignal> responseStream,
        ServerCallContext context)
    {
        var cts = context.CancellationToken;

        await responseStream.WriteAsync(new UiStateSignal
        {
            Viewport = new UiViewportSignal { ZoomDepth = 1.0, CenterX = 0, CenterY = 0 }
        }, cts);

        await foreach (var input in requestStream.ReadAllAsync(cts))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(input.InputPayload))
                {
                    await DispatchActionFromPayloadAsync(input.InputPayload);
                }
                else if (!string.IsNullOrWhiteSpace(input.TargetNeuronId))
                {
                    var neuron = NeuronResolver.Resolve(grainFactory, input.TargetNeuronId);
                    await neuron.FireAsync(new DemoMessageSynapse("ui-input:" + input.ElementId));
                }

                var signal = new UiStateSignal
                {
                    Viewport = new UiViewportSignal
                    {
                        ZoomDepth = 1.0,
                        CenterX = input.Coordinates?.X ?? 0,
                        CenterY = input.Coordinates?.Y ?? 0
                    }
                };
                await responseStream.WriteAsync(signal, cts);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "UiGateway input handling non-fatal");
            }
        }
    }

    private async Task DispatchActionFromPayloadAsync(string payloadJson)
    {
        string? synapseType = null;
        Dictionary<string, object?> props = new();

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("synapseType", out var st) || root.TryGetProperty("SynapseType", out st))
                synapseType = st.GetString();
            if (root.TryGetProperty("props", out var p) || root.TryGetProperty("Props", out p))
            {
                if (p.ValueKind == JsonValueKind.Object)
                    props = JsonSerializer.Deserialize<Dictionary<string, object?>>(p.GetRawText()) ?? new();
            }
            if (string.IsNullOrWhiteSpace(synapseType) && root.TryGetProperty("action", out var act) && act.ValueKind == JsonValueKind.Object)
            {
                if (act.TryGetProperty("synapseType", out st)) synapseType = st.GetString();
                if (act.TryGetProperty("props", out p) && p.ValueKind == JsonValueKind.Object)
                    props = JsonSerializer.Deserialize<Dictionary<string, object?>>(p.GetRawText()) ?? new();
            }
        }
        catch { /* not json action, fallthrough */ }

        if (string.IsNullOrWhiteSpace(synapseType))
        {
            // generic from shell/kit event
            var n = NeuronResolver.Resolve(grainFactory, "ino-main");
            await n.FireAsync(new DemoMessageSynapse(payloadJson));
            return;
        }

        switch (synapseType)
        {
            case nameof(InoRequest):
                var prompt = GetProp(props, "prompt") ?? GetProp(props, "text") ?? payloadJson;
                await grainFactory.GetGrain<IInoNeuron>("ino-main").FireAsync(new InoRequest(prompt, GetProp(props, "sessionId")));
                return;
            case nameof(InstallFromMarketplace):
                var pack = GetProp(props, "packName") ?? GetProp(props, "name") ?? "";
                var ver = GetProp(props, "version") ?? "0.1.0";
                var buyer = GetProp(props, "buyerId") ?? "current-user";
                await grainFactory.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new InstallFromMarketplace(pack, ver, buyer));
                return;
            case nameof(PublishToMarketplace):
                var pName = GetProp(props, "packName") ?? GetProp(props, "name") ?? "";
                var pVer = GetProp(props, "version") ?? "1.0.0";
                var pCode = GetProp(props, "code") ?? "";
                var pOwner = GetProp(props, "ownerId") ?? "current-user";
                var pPrivate = bool.TryParse(GetProp(props, "isPrivate"), out var priv) && priv;
                var pComm = double.TryParse(GetProp(props, "commissionRate"), out var comm) ? comm : 0.0;
                var pDesc = GetProp(props, "description") ?? "";
                await grainFactory.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new PublishToMarketplace(pName, pVer, pCode, pOwner, pPrivate, pComm, pDesc));
                return;
            case nameof(RestartResource):
                var res = GetProp(props, "resourceName");
                if (!string.IsNullOrWhiteSpace(res))
                    await grainFactory.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new RestartResource(res));
                return;
            default:
                var target = GetProp(props, "neuronId") ?? "ino-main";
                await NeuronResolver.Resolve(grainFactory, target).FireAsync(new DemoMessageSynapse(payloadJson));
                return;
        }
    }

    private static string? GetProp(Dictionary<string, object?> p, string key) =>
        p.TryGetValue(key, out var v) ? v?.ToString() : null;
}
