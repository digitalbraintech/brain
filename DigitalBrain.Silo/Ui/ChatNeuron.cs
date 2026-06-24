using System.Text.Json;
using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// The Chat neuron (server-driven UI). On a data-visualization request it emits an RfwCard, journals it as
// conversation history, and broadcasts it to the live Home feed (HomeFeedBus) for streaming to clients.
[GrainType("digitalbrain.chat.v1")]
public class ChatNeuron : Neuron, IChatNeuron
{
    public ChatNeuron(ILogger<ChatNeuron> logger) : base(logger) { }

    public async Task HandleAsync(VisualizeDataRequest request)
    {
        var dataJson = JsonSerializer.Serialize(new
        {
            prompt = request.Prompt,
            data = request.DataJson,
            chartHint = request.ChartHint
        });
        var card = new RfwCard("digitalbrain", "DataChartCard", dataJson);

        await FireAsync(card);                               // record in conversation history (journal)
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(card);  // push to the live UI feed
    }

    public Task<RfwCard[]> GetConversationAsync()
        => Task.FromResult(OutgoingJournal.OfType<RfwCard>().ToArray());
}
