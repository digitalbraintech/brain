using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GmailAgentTests
{
    [Fact]
    public async Task Ino_delegates_to_real_principal_scoped_Gmail_with_native_tools_and_source_owned_routes()
    {
        var client = new GmailChatClient();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(DigitalBrain.Execution.ExecutionModule), typeof(DigitalBrain.UI.UIModule), typeof(AIModule), typeof(GoogleModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(client),
        });
        var assistant = simulation.Brain.Get<IAssistant>("assistant").Id;
        var actor = new ActorContext(PrincipalId.New(), "first");
        var other = new ActorContext(PrincipalId.New(), "second");
        foreach (var principal in new[] { actor, other })
        {
            using var verified = VerifiedActor.Enter(principal);
            var reply = await simulation.Grains.GetGrain<IAgentKernel>(assistant.ToGrainId())
                .Ask(new AgentRequest("find customer email"), CorrelationId.New(), TestContext.Current.CancellationToken);
            Assert.Contains("thread-intochat", reply.Text, StringComparison.Ordinal);
            var target = NeuronId.For<IGmail>(assistant.Owner, PrincipalPartition.InstanceName(principal.PrincipalId, "gmail-local"));
            var source = simulation.Grains.GetGrain<INeuronQuery>(assistant.ToGrainId());
            var route = Assert.Single(await source.ReadSynapses(), edge => edge.Target == target);
            Assert.Equal(SynapseKind.Learned, route.Kind);
            Assert.Equal(nameof(AgentRequest), route.SignalType);
            var incoming = await simulation.Grains.GetGrain<INeuronQuery>(target.ToGrainId()).ReadJournal(JournalKind.Incoming, 0);
            var request = Assert.Single(incoming.Delta, delivery => delivery.Signal is AgentRequest);
            Assert.Equal(assistant, request.Caller);
            Assert.Equal(principal.PrincipalId, request.Principal);
        }
        Assert.Equal(2, client.AssistantCatalogs.Count);
        Assert.Equal(2, client.GmailCatalogs.Count);
        Assert.All(client.AssistantCatalogs, names =>
        {
            Assert.Contains("ask_gmail", names);
            Assert.DoesNotContain("search_threads", names);
            Assert.DoesNotContain("gmail_search_threads", names);
        });
        Assert.All(client.GmailCatalogs, names =>
        {
            Assert.Contains("search_threads", names);
            Assert.DoesNotContain("ask_gmail", names);
        });
    }

    private sealed class GmailChatClient : IChatClient
    {
        internal List<string[]> AssistantCatalogs { get; } = [];
        internal List<string[]> GmailCatalogs { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var tools = options?.Tools?.OfType<AIFunction>().ToArray() ?? [];
            object? result;
            if (messages.Any(message => message.Role == ChatRole.System && message.Text.StartsWith("You are Gmail,", StringComparison.Ordinal)))
            {
                GmailCatalogs.Add(tools.Select(tool => tool.Name).ToArray());
                result = await Assert.Single(tools, tool => tool.Name == "search_threads")
                    .InvokeAsync(new() { ["query"] = "customer" }, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                AssistantCatalogs.Add(tools.Select(tool => tool.Name).ToArray());
                result = await Assert.Single(tools, tool => tool.Name == "ask_gmail")
                    .InvokeAsync(new() { ["request"] = "find customer email" }, cancellationToken).ConfigureAwait(true);
            }
            yield return new ChatResponseUpdate(ChatRole.Assistant, result?.ToString()) { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
