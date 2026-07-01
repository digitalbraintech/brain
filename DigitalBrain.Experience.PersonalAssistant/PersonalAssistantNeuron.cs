using DigitalBrain.Core;

namespace DigitalBrain.Experience.PersonalAssistant;

// Pure pack: composes Telegram + Context + LLM purely via generic Signal names, never a typed
// reference to DigitalBrain.Telegram/.Context/.UiKit. Mirrors TelegramResponderNeuron's shape.
public sealed class PersonalAssistantNeuron : IPackBehavior
{
    private const string ConfigPack = "DigitalBrain.Experience.PersonalAssistant";
    private const string ConfigScope = "default";

    public PackManifest GetManifest() => new(
        new[]
        {
            new SynapseType(TelegramSignals.MessageReceived),
            new SynapseType(ContextSignals.RecallCompleted),
            new SynapseType(PersonalAssistantSignals.LlmReplyReady)
        },
        new PackConfigField[]
        {
            new("llm_provider", "LLM",     PackConfigFieldKind.Choice, new[] { "ollama", "openai" }),
            new("llm_key",      "API key", PackConfigFieldKind.Secret,
                DependsOnKey: "llm_provider", DependsOnValue: "openai"),
        });

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        switch (synapse)
        {
            case Signal s when s.Name == TelegramSignals.MessageReceived:
                var query = s.Props.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
                var chatId = s.Props.TryGetValue("chatId", out var c) ? c : null;
                return new Synapse[]
                {
                    new Signal(ContextSignals.RecallRequested,
                        new Dictionary<string, object?> { ["query"] = query, ["chatId"] = chatId })
                };

            case Signal s when s.Name == ContextSignals.RecallCompleted:
                var results = s.Props.TryGetValue("results", out var r) ? r as string[] ?? [] : [];
                var augmentedPrompt = results.Length > 0
                    ? $"Context: {string.Join("; ", results)}\n\nRespond helpfully."
                    : "Respond helpfully.";
                // Known gap: ContextSignals.RecallCompleted carries only {results}, not the chatId this
                // pack passed into RecallRequested, so it cannot be threaded into ReplyProps here. Until
                // ContextNeuron echoes caller-supplied correlation data back, LlmReplyReady's chatId (and
                // therefore the final TelegramReplyRequested) resolves to null in a real deployment.
                return new Synapse[]
                {
                    new AskLlm(augmentedPrompt, PersonalAssistantSignals.LlmReplyReady, new Dictionary<string, object?>(), ConfigPack, ConfigScope)
                };

            case Signal s when s.Name == PersonalAssistantSignals.LlmReplyReady:
                var text = s.Props.TryGetValue("text", out var rt) ? rt?.ToString() ?? "" : "";
                var replyChatId = s.Props.TryGetValue("chatId", out var rc) ? rc : null;
                return new Synapse[]
                {
                    new Signal(TelegramSignals.ReplyRequested,
                        new Dictionary<string, object?> { ["chatId"] = replyChatId, ["text"] = text })
                };

            default:
                return Array.Empty<Synapse>();
        }
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Content,
        null,
        new[] { BundleChannel.Telegram },
        new[]
        {
            new BundleDependency("DigitalBrain.Telegram.Responder", "1.0.0"),
            new BundleDependency("DigitalBrain.UIKit.ForUI", "0.1.0")
        });
}

internal static class PersonalAssistantSignals
{
    public const string LlmReplyReady = "PersonalAssistantLlmReplyReady";
}
