using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.UiKit;

namespace DigitalBrain.Kernel;

// The Chat neuron (server-driven UI). On a data-visualization request it emits an RfwCard, journals it as
// conversation history, and broadcasts it to the live Home feed (HomeFeedBus) for streaming to clients.
[GrainType("digitalbrain.chat.v1")]
public class ChatNeuron(ILogger<ChatNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IChatNeuron
{
    public async Task HandleAsync(VisualizeDataRequest request)
    {
        var dataJson = JsonSerializer.Serialize(new
        {
            prompt = request.Prompt,
            data = request.DataJson,
            chartHint = request.ChartHint
        });
        var card = new RfwCard("digitalbrain", "DataChartCard", dataJson);

        await FireAsync(card);  // keep for conversation journal compat
        // Prefer routing through IFlutterUiNeuron (item 14) for dedicated UI channel + context.
        var surface = new UiSurface(UiSurfaceKinds.DataChart, new Dictionary<string, object?>
        {
            ["prompt"] = request.Prompt,
            ["data"] = request.DataJson,
            ["chartHint"] = request.ChartHint
        });
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    public Task<RfwCard[]> GetConversationAsync()
        => Task.FromResult(OutgoingJournal.OfType<RfwCard>().ToArray());
}

