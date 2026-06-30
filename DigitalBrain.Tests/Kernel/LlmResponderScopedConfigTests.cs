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

    public IChatClient? Create(string provider, string? apiKey)
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

    // Stores config through the silo's IPackConfigStore. The fast-path store keeps config in a per-silo
    // in-memory backing dictionary encrypted with that silo's ephemeral DataProtection keys, so the emitter
    // and responder must share a silo to read the same plaintext — the tests pin a single-silo cluster.
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

// Returns null for every Create call — simulates the graceful-fallback path (e.g. openai with no key).
public sealed class NullScopedChatClientFactory : IScopedChatClientFactory
{
    public readonly List<(string Provider, string? ApiKey)> Requests = new();

    public IChatClient? Create(string provider, string? apiKey)
    {
        Requests.Add((provider, apiKey));
        return null;
    }
}

// Wires the NullScopedChatClientFactory + global AnswerPrefixChatClient + real in-memory PackConfigStore.
// The null factory forces the responder to fall back to the global client.
public sealed class NullScopedLlmResponderSiloConfigurator : ISiloConfigurator
{
    public static readonly NullScopedChatClientFactory Factory = new();

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

        // Single silo: the in-memory PackConfigStore lives per silo, so emitter and responder must co-locate
        // to share the stored config (and its silo-local DataProtection keys). The default 2-silo cluster
        // places them nondeterministically, which made this test flaky (responder read an empty store → global).
        var builder = new TestClusterBuilder(initialSilosCount: 1);
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

        // Single silo keeps the responder co-located with the emitter (see the scoped test for why).
        var builder = new TestClusterBuilder(initialSilosCount: 1);
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

    [Fact]
    public async Task AskLlm_scoped_factory_returns_null_falls_back_to_global_client()
    {
        NullScopedLlmResponderSiloConfigurator.Factory.Requests.Clear();

        // Single silo so the responder reads the same in-memory config the emitter stored (see the scoped test).
        var builder = new TestClusterBuilder(initialSilosCount: 1);
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        builder.AddSiloBuilderConfigurator<NullScopedLlmResponderSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            const string pack = "DigitalBrain.Telegram.Responder";
            const string scope = "default";

            var responder = cluster.GrainFactory.GetGrain<ILlmResponderNeuron>("responder-nullfactory-1");
            await responder.GetTimelineAsync();

            var emitter = cluster.GrainFactory.GetGrain<IScopedAskLlmEmitter>("emitter-nullfactory-1");
            // Store openai config but with no key — factory will be asked and return null.
            await emitter.StoreConfigAsync(scope, pack, new Dictionary<string, string>
            {
                ["llm_provider"] = "openai",
                ["llm_key"] = "",
            });
            var replyProps = new Dictionary<string, object?> { ["chatId"] = 42 };
            await emitter.BroadcastScopedAskAsync("hi", "TelegramReplyFallback", replyProps, pack, scope);

            // Should still get a reply — from the global AnswerPrefixChatClient, not silence.
            Signal? signal = null;
            for (var attempt = 0; attempt < 20 && signal is null; attempt++)
            {
                await Task.Delay(50);
                var timeline = await responder.GetTimelineAsync();
                signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "TelegramReplyFallback");
            }

            Assert.NotNull(signal);
            Assert.Equal("ANSWER:hi", signal.Props["text"]);
            // Factory was called (it attempted to build) but returned null.
            var request = Assert.Single(NullScopedLlmResponderSiloConfigurator.Factory.Requests);
            Assert.Equal("openai", request.Provider);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
