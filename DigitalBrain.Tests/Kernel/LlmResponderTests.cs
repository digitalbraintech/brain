using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

// Emitter grain that broadcasts AskLlm so the responder can receive it from the timeline.
public interface IAskLlmEmitter : INeuron
{
    Task BroadcastAskAsync(string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps);
}

public sealed class AskLlmEmitter : Neuron, IAskLlmEmitter
{
    public AskLlmEmitter(Microsoft.Extensions.Logging.ILogger<AskLlmEmitter> logger, NeuronJournals journals)
        : base(logger, journals) { }

    public Task BroadcastAskAsync(string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps) =>
        Broadcast(new AskLlm(prompt, replyType, replyProps));
}

// Deterministic fake: returns "ANSWER:" + prompt, zero external I/O.
internal sealed class AnswerPrefixChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Concat(messages.Select(m => m.Text));
        var reply = new ChatMessage(ChatRole.Assistant, "ANSWER:" + prompt);
        return Task.FromResult(new ChatResponse(reply));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not used.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

// Test-local configurator: applies the shared silo wiring then registers the fake IChatClient.
// Using both configurators via AddSiloBuilderConfigurator keeps LlmResponderTests isolated —
// other test clusters that omit this configurator get no IChatClient (their deterministic fallback).
public sealed class LlmResponderSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.ConfigureServices(services =>
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>());
}

public class LlmResponderTests
{
    [Fact]
    public async Task AskLlm_broadcast_triggers_reply_Signal_with_llm_text()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        builder.AddSiloBuilderConfigurator<LlmResponderSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            // Activate responder so it subscribes to the timeline before the ask arrives.
            var responder = cluster.GrainFactory.GetGrain<ILlmResponderNeuron>("responder-1");
            await responder.GetTimelineAsync();

            var emitter = cluster.GrainFactory.GetGrain<IAskLlmEmitter>("emitter-1");
            var replyProps = new Dictionary<string, object?> { ["chatId"] = 7 };
            await emitter.BroadcastAskAsync("hi", "ReplyX", replyProps);

            // Poll the responder's timeline: stream delivery + grain dispatch cross the silo scheduler,
            // so a fixed delay is flaky under load. Bounded wait keeps the test fast when it lands quickly.
            Signal? signal = null;
            for (var attempt = 0; attempt < 20 && signal is null; attempt++)
            {
                await Task.Delay(50);
                var timeline = await responder.GetTimelineAsync();
                signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "ReplyX");
            }

            Assert.NotNull(signal);
            Assert.Equal("ReplyX", signal.Name);
            Assert.Equal(7, signal.Props["chatId"]);
            Assert.Equal("ANSWER:hi", signal.Props["text"]);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
