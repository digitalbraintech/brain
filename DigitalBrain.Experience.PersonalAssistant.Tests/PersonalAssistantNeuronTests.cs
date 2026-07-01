using DigitalBrain.Core;
using DigitalBrain.Experience.PersonalAssistant;
using Xunit;

namespace DigitalBrain.Experience.PersonalAssistant.Tests;

public class PersonalAssistantNeuronTests
{
    [Fact]
    public void TelegramMessage_Produces_ContextRecallRequested()
    {
        var pack = new PersonalAssistantNeuron();
        var inbound = new Signal(TelegramSignals.MessageReceived,
            new Dictionary<string, object?> { ["text"] = "when is the launch?", ["chatId"] = 123L });

        var outputs = pack.Handle(inbound);

        var recall = Assert.Single(outputs);
        var signal = Assert.IsType<Signal>(recall);
        Assert.Equal(ContextSignals.RecallRequested, signal.Name);
        Assert.Equal("when is the launch?", signal.Props["query"]);
    }

    [Fact]
    public void ContextRecallCompleted_Produces_AskLlm_With_Augmented_Prompt()
    {
        var pack = new PersonalAssistantNeuron();
        var recalled = new Signal(ContextSignals.RecallCompleted,
            new Dictionary<string, object?>
            {
                ["results"] = new[] { "the launch date is March 5th" },
                ["chatId"] = 123L
            });

        var outputs = pack.Handle(recalled);

        var ask = Assert.IsType<AskLlm>(Assert.Single(outputs));
        Assert.Contains("the launch date is March 5th", ask.Prompt);
        Assert.Equal(123L, ask.ReplyProps["chatId"]);
    }

    [Fact]
    public void LlmReply_Produces_TelegramReplyRequested()
    {
        var pack = new PersonalAssistantNeuron();
        var llmReply = new Signal("PersonalAssistantLlmReplyReady",
            new Dictionary<string, object?> { ["text"] = "March 5th", ["chatId"] = 123L });

        var outputs = pack.Handle(llmReply);

        var reply = Assert.IsType<Signal>(Assert.Single(outputs));
        Assert.Equal(TelegramSignals.ReplyRequested, reply.Name);
        Assert.Equal("March 5th", reply.Props["text"]);
        Assert.Equal(123L, reply.Props["chatId"]);
    }

    [Fact]
    public void GetBundleManifest_Declares_Telegram_And_UiKit_Dependencies()
    {
        var manifest = new PersonalAssistantNeuron().GetBundleManifest();
        Assert.NotNull(manifest);
        Assert.Contains(manifest!.Dependencies!, d => d.PackName == "DigitalBrain.Telegram.Responder");
        Assert.Contains(manifest.Dependencies!, d => d.PackName == "DigitalBrain.UIKit.ForUI");
    }
}
