namespace DigitalBrain.Protocol;

// A Remote Flutter Widgets payload pushed from a neuron to the Home feed. DataJson is an opaque blob (RFW data
// is dynamic, so there is no static schema). This is the second server-driven-UI payload kind alongside UiSurface
// (the canonical SDUI model stays UiSurface; RfwCard is added for the streaming RFW feed). Harvested from digitalbrain.
[GenerateSerializer]
public record RfwCard(string LibraryName, string RootWidget, string DataJson)
    : Synapse(nameof(RfwCard), DateTimeOffset.UtcNow);

// The Chat neuron: handles a data-visualization request and emits an RfwCard for the live UI feed.
// Its journal is the conversation history (MAIN keeps history in the journal, not a separate ConversationGrain).
public interface IChatNeuron : INeuron, IHandle<VisualizeDataRequest>
{
    Task<RfwCard[]> GetConversationAsync();
}
