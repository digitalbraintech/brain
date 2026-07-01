using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// One grain per Telegram chat (key "tg-chat-<chatId>"). It is the entry point for inbound Telegram messages.
// The chat's bound bundle is derived from the durable incoming journal (the most recent "/start <bundleId>"),
// so binding needs no separate state store. A bound chat routes normal messages point-to-point to the bound
// bundle's generated neuron; an unbound chat broadcasts (today's behaviour → the seeded Responder handles it).
[GrainType("digitalbrain.telegram-chat.v1")]
public sealed class TelegramChatNeuron : Neuron, ITelegramChatNeuron, IHandle<Signal>
{
    private const string InboundName = "TelegramMessageReceived";
    private const string ReplyName = "TelegramReplyRequested";
    private const string StartPrefix = "/start";

    // Purely point-to-point driven (fed by the gateway via DeliverAsync). It EMITS broadcasts
    // (confirmation, unbound fallback) but must never RECEIVE timeline echoes — otherwise its own
    // broadcast of a "TelegramMessageReceived" signal would loop back into HandleAsync.
    protected override bool ShouldSubscribeToTimeline => false;

    public TelegramChatNeuron(ILogger<TelegramChatNeuron> logger, NeuronJournals journals)
        : base(logger, journals) { }

    public Task<string?> GetBoundBundleAsync() => Task.FromResult(BoundBundle());

    public async Task HandleAsync(Signal signal)
    {
        if (signal.IsBroadcast) return;
        if (signal.Name != InboundName) return;

        var text = signal.Props.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
        var chatId = signal.Props.TryGetValue("chatId", out var c) ? c : null;

        if (TryParseStart(text, out var bundleId))
        {
            // The binding itself is implicit: this "/start" is now the most recent one in the journal.
            await Broadcast(new Signal(ReplyName, new Dictionary<string, object?>
            {
                ["chatId"] = chatId,
                ["text"] = $"You're now chatting with {bundleId}."
            }));
            return;
        }

        var bound = BoundBundle();
        if (bound is not null)
        {
            var receiver = new NeuronId("generated-" + bound.ToLowerInvariant());
            // FireAsync routes point-to-point via GetGrain<INeuron>() which is ambiguous across all grain types.
            // Generated neurons are always IGeneratedNeuron, so journal + deliver via that interface directly.
            var stamped = (signal with { Receiver = receiver }).Stamp(Self, CurrentCause);
            OutgoingJournal.Add(stamped);
            await WriteStateAsync();
            await GrainFactory.GetGrain<IGeneratedNeuron>(receiver.Value).DeliverAsync(stamped);
        }
        else
        {
            await Broadcast(signal);
        }
    }

    // Most recent "/start <bundleId>" in the incoming journal is the active binding; null if none.
    private string? BoundBundle()
    {
        for (var i = IncomingJournal.Count - 1; i >= 0; i--)
        {
            if (IncomingJournal[i] is Signal s && s.Name == InboundName
                && s.Props.TryGetValue("text", out var t)
                && TryParseStart(t?.ToString() ?? "", out var bundleId))
            {
                return bundleId;
            }
        }
        return null;
    }

    private static bool TryParseStart(string text, out string bundleId)
    {
        bundleId = "";
        var trimmed = text.Trim();
        // "/startfoo" must NOT match — require exactly "/start" or "/start" followed by whitespace.
        if (trimmed.Length != StartPrefix.Length
            && (trimmed.Length < StartPrefix.Length || !trimmed.StartsWith(StartPrefix, StringComparison.Ordinal) || !char.IsWhiteSpace(trimmed[StartPrefix.Length])))
            return false;
        if (trimmed.Length == StartPrefix.Length) return false; // bare "/start" with no payload
        var rest = trimmed[StartPrefix.Length..].Trim();
        if (rest.Length == 0) return false;
        bundleId = rest;
        return true;
    }
}
