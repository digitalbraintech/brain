using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

// Records the (provider, apiKey) it is asked for and returns a stub that prefixes "SCOPED:".
// Lets the test prove the responder read provider/key from the store and used the scoped client,
// never the global IChatClient.
public sealed class RecordingScopedChatClientFactory : IScopedChatClientFactory
{
    public readonly List<(string Provider, string? ApiKey)> Requests = new();

    public IChatClient Create(string provider, string? apiKey)
    {
        Requests.Add((provider, apiKey));
        return new ScopedPrefixChatClient();
    }
}

internal sealed class ScopedPrefixChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Concat(messages.Select(m => m.Text));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "SCOPED:" + prompt)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not used.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

// Emitter that broadcasts an AskLlm carrying a config reference.
public interface IScopedAskLlmEmitter : INeuron
{
    Task BroadcastScopedAskAsync(
        string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps,
        string? configPack, string? configScope);

    // Stores config through the SILO's IPackConfigStore so the responder grain (also in the silo)
    // reads the same backing store instance.
    Task StoreConfigAsync(string scope, string pack, Dictionary<string, string> values);
}

public sealed class ScopedAskLlmEmitter : Neuron, IScopedAskLlmEmitter
{
    public ScopedAskLlmEmitter(Microsoft.Extensions.Logging.ILogger<ScopedAskLlmEmitter> logger, NeuronJournals journals)
        : base(logger, journals) { }

    public Task BroadcastScopedAskAsync(
        string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps,
        string? configPack, string? configScope) =>
        Broadcast(new AskLlm(prompt, replyType, replyProps, configPack, configScope));

    public Task StoreConfigAsync(string scope, string pack, Dictionary<string, string> values) =>
        ServiceProvider.GetRequiredService<IPackConfigStore>().SetAsync(scope, pack, values);
}

// Wires the global AnswerPrefixChatClient (proves it is NOT used on the scoped path), a real
// in-memory PackConfigStore, and the recording scoped factory shared via a static so the test can assert on it.
public sealed class ScopedLlmResponderSiloConfigurator : ISiloConfigurator
{
    public static readonly RecordingScopedChatClientFactory Factory = new();

    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
            services.AddSingleton<IScopedChatClientFactory>(Factory);
            services.AddPackConfigStore(blobsForKeyRing: null);
        });
}

public class LlmResponderScopedConfigTests
{
    [Fact]
    public async Task AskLlm_with_ConfigPack_uses_scoped_client_from_stored_provider_and_key()
    {
        ScopedLlmResponderSiloConfigurator.Factory.Requests.Clear();

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        builder.AddSiloBuilderConfigurator<ScopedLlmResponderSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            const string pack = "DigitalBrain.Telegram.Responder";
            const string scope = "default";

            var responder = cluster.GrainFactory.GetGrain<ILlmResponderNeuron>("responder-scoped-1");
            await responder.GetTimelineAsync();

            var emitter = cluster.GrainFactory.GetGrain<IScopedAskLlmEmitter>("emitter-scoped-1");
            await emitter.StoreConfigAsync(scope, pack, new Dictionary<string, string>
            {
                ["llm_provider"] = "openai",
                ["llm_key"] = "sk-test",
            });
            var replyProps = new Dictionary<string, object?> { ["chatId"] = 7 };
            await emitter.BroadcastScopedAskAsync("hi", "TelegramReplyRequested", replyProps, pack, scope);

            Signal? signal = null;
            for (var attempt = 0; attempt < 20 && signal is null; attempt++)
            {
                await Task.Delay(50);
                var timeline = await responder.GetTimelineAsync();
                signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "TelegramReplyRequested");
            }

            Assert.NotNull(signal);
            Assert.Equal("SCOPED:hi", signal.Props["text"]);

            var request = Assert.Single(ScopedLlmResponderSiloConfigurator.Factory.Requests);
            Assert.Equal("openai", request.Provider);
            Assert.Equal("sk-test", request.ApiKey);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task AskLlm_without_ConfigPack_uses_global_client()
    {
        ScopedLlmResponderSiloConfigurator.Factory.Requests.Clear();

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        builder.AddSiloBuilderConfigurator<ScopedLlmResponderSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var responder = cluster.GrainFactory.GetGrain<ILlmResponderNeuron>("responder-global-1");
            await responder.GetTimelineAsync();

            var emitter = cluster.GrainFactory.GetGrain<IScopedAskLlmEmitter>("emitter-global-1");
            var replyProps = new Dictionary<string, object?> { ["chatId"] = 9 };
            await emitter.BroadcastScopedAskAsync("hi", "ReplyGlobal", replyProps, configPack: null, configScope: null);

            Signal? signal = null;
            for (var attempt = 0; attempt < 20 && signal is null; attempt++)
            {
                await Task.Delay(50);
                var timeline = await responder.GetTimelineAsync();
                signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "ReplyGlobal");
            }

            Assert.NotNull(signal);
            Assert.Equal("ANSWER:hi", signal.Props["text"]);
            Assert.Empty(ScopedLlmResponderSiloConfigurator.Factory.Requests);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
