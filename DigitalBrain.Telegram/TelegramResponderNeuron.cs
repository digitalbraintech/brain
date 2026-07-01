using DigitalBrain.Core;

namespace DigitalBrain.Telegram;

// Pure pack: no System.Net, no Process, no Reflection.Emit. Passes CapabilityGate.
// Works with Signal (name+props), never with the transport-typed TelegramMessageReceived record
// — keeping the kernel/pack layer decoupled from transport specifics.
public sealed class TelegramResponderNeuron : IPackBehavior
{
    // Marketplace NeuroPack name + scope this pack's config is stored under; the responder reads the
    // user's chosen LLM provider/key from (ConfigScope, ConfigPack) to route the AskLlm.
    private const string ConfigPack = "DigitalBrain.Telegram.Responder";
    private const string ConfigScope = "default";

    public PackManifest GetManifest() => new(
        new[] { new SynapseType(TelegramSignals.MessageReceived) },
        new PackConfigField[]
        {
            new("telegram_token", "Bot token",    PackConfigFieldKind.Secret),
            new("llm_provider",   "LLM",          PackConfigFieldKind.Choice, new[] { "ollama", "openai" }),
            new("llm_key",        "API key",       PackConfigFieldKind.Secret,
                DependsOnKey: "llm_provider", DependsOnValue: "openai"),
        });

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not Signal s || s.Name != TelegramSignals.MessageReceived)
            return Array.Empty<Synapse>();

        var text    = s.Props.TryGetValue("text",   out var t) ? t?.ToString() ?? "" : "";
        var chatId  = s.Props.TryGetValue("chatId", out var c) ? c : null;

        return new Synapse[]
        {
            new AskLlm(
                text,
                TelegramSignals.ReplyRequested,
                new Dictionary<string, object?> { ["chatId"] = chatId },
                ConfigPack,
                ConfigScope)
        };
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Channel,
        null,
        new[] { BundleChannel.Telegram });
}
