using DigitalBrain.TestKit;
using DigitalBrain.Context;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Context.Tests;

public class ContextNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Remember_Then_Recall_Finds_The_Text()
    {
        var context = _brain.Grain<IContextNeuron>("context-recall-test");
        await context.RememberAsync("the sky is blue today");
        var results = await context.RecallAsync("sky color");
        Assert.Contains("the sky is blue today", results);
    }

    [Fact]
    public async Task Signal_RecallRequested_Replies_With_Signal_RecallCompleted()
    {
        var context = _brain.Grain<IContextNeuron>("context-signal-test");
        await context.RememberAsync("the launch date is March 5th");

        await context.DeliverAsync(new Signal(
            ContextSignals.RecallRequested,
            new Dictionary<string, object?> { ["query"] = "launch date", ["chatId"] = 123L })
        { Receiver = new NeuronId("context-signal-test") });

        var outgoing = await context.GetTimelineAsync();
        Assert.Contains(outgoing, s => s is Signal reply
            && reply.Name == ContextSignals.RecallCompleted
            && reply.Props.TryGetValue("results", out var r)
            && r is string[] results && results.Contains("the launch date is March 5th")
            && reply.Props.TryGetValue("chatId", out var c) && Equals(c, 123L)
            && !reply.Props.ContainsKey("query"));
    }
}
