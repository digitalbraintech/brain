using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Telegram;
using Xunit;

namespace DigitalBrain.Tests.Telegram;

public class ResponderPackTests
{
    // ---- Unit tests: work against the compiled type in DigitalBrain.Telegram ----

    [Fact]
    public void GetManifest_Handles_TelegramMessageReceived()
    {
        var neuron = new TelegramResponderNeuron();
        var manifest = neuron.GetManifest();

        Assert.Contains(new SynapseType("TelegramMessageReceived"), manifest.HandledSynapseTypes);
    }

    [Fact]
    public void GetManifest_Has_Three_RequiredConfig_Fields()
    {
        var manifest = new TelegramResponderNeuron().GetManifest();

        Assert.NotNull(manifest.RequiredConfig);
        Assert.Equal(3, manifest.RequiredConfig!.Count);
        Assert.Contains(manifest.RequiredConfig, f => f.Key == "telegram_token" && f.Kind == PackConfigFieldKind.Secret);
        Assert.Contains(manifest.RequiredConfig, f => f.Key == "llm_provider"   && f.Kind == PackConfigFieldKind.Choice);
        Assert.Contains(manifest.RequiredConfig, f => f.Key == "llm_key"        && f.DependsOnKey == "llm_provider" && f.DependsOnValue == "openai");
    }

    [Fact]
    public void Handle_TelegramMessageReceived_Signal_Returns_AskLlm()
    {
        var neuron = new TelegramResponderNeuron();
        var signal = new Signal("TelegramMessageReceived",
            new Dictionary<string, object?> { ["chatId"] = 7L, ["text"] = "hi" });

        var results = neuron.Handle(signal);

        var ask = Assert.IsType<AskLlm>(Assert.Single(results));
        Assert.Equal("hi",                      ask.Prompt);
        Assert.Equal("TelegramReplyRequested",  ask.ReplyType);
        Assert.Equal(7L,                        ask.ReplyProps["chatId"]);
    }

    [Fact]
    public void Handle_NonMatching_Signal_Returns_Empty()
    {
        var neuron = new TelegramResponderNeuron();
        var signal = new Signal("SomeOtherSignal", new Dictionary<string, object?> { ["x"] = 1 });

        Assert.Empty(neuron.Handle(signal));
    }

    // ---- Embodiment test: prove the pack-source string in MarketplaceSeeds compiles via real Roslyn/ALC ----

    [Fact]
    public void TelegramResponderPackCode_Compiles_And_Embodies_Via_PackAlcEmbodier()
    {
        var embodier = new PackAlcEmbodier();
        var embodied = embodier.Embody("TelegramResponderNeuron", MarketplaceSeeds.TelegramResponderPackCode);

        Assert.NotNull(embodied);

        // Sanity: manifest survives the ALC boundary
        var manifest = embodied.GetManifest();
        Assert.Contains(new SynapseType("TelegramMessageReceived"), manifest.HandledSynapseTypes);

        embodied.Dispose();
    }

    [Fact]
    public void TelegramResponderPackCode_Passes_CapabilityGate()
    {
        // CapabilityGate rejects System.Net / Process / Reflection.Emit. Successful Embody proves it passes.
        var embodier = new PackAlcEmbodier();
        var embodied = embodier.Embody("TelegramResponderNeuron", MarketplaceSeeds.TelegramResponderPackCode);
        embodied.Dispose();
    }
}
