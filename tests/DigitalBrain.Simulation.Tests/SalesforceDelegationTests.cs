using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SalesforceDelegationTests
{
    [Fact]
    public async Task Ino_routes_generic_request_to_actual_Salesforce_neuron_with_its_native_catalog()
    {
        var client = new SalesforceModel();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(DigitalBrain.Execution.ExecutionModule), typeof(DigitalBrain.UI.UIModule),
                typeof(AIModule), typeof(SalesforceModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(client),
        });
        var actor = new ActorContext(PrincipalId.New(), "salesforce-owner");
        var ino = simulation.Brain.Get<IAssistant>("assistant").Id;
        var target = NeuronId.For<ISalesforce>(ino.Owner, PrincipalPartition.InstanceName(actor.PrincipalId, "salesforce-local"));
        using var verified = VerifiedActor.Enter(actor);
        var reply = await simulation.Grains.GetGrain<IAgentKernel>(IAgentKernel.IdFor(ino.Owner))
            .Ask(new AgentRequest("Who is connected to Salesforce?"), CorrelationId.New(), TestContext.Current.CancellationToken);

        Assert.Contains("Salesforce fixture", reply.Text, StringComparison.Ordinal);
        Assert.Equal(actor.PrincipalId, client.SpecialistPrincipal);
        Assert.Contains("ask_salesforce", client.InoTools);
        Assert.DoesNotContain(client.InoTools, tool => SalesforceMcp.AllowedTools.Contains(tool));
        Assert.Equal(SalesforceLogins.ReadTools.Order(), client.SpecialistTools.Order());
        var source = simulation.Grains.GetGrain<INeuronQuery>(ino.ToGrainId());
        var route = Assert.Single(await source.ReadSynapses(), synapse => synapse.Target == target && synapse.SignalType == nameof(AgentRequest));
        Assert.Equal(SynapseKind.Learned, route.Kind);
        var requests = (await simulation.Grains.GetGrain<INeuronQuery>(target.ToGrainId()).ReadJournal(JournalKind.Incoming, 0))
            .Delta.Where(delivery => delivery.Signal is AgentRequest).ToArray();
        Assert.Equal(ino, Assert.Single(requests).Caller);
        Assert.Equal(actor.PrincipalId, requests[0].Principal);
    }

    private sealed class SalesforceModel : IChatClient
    {
        internal string[] InoTools = [];
        internal string[] SpecialistTools = [];
        internal PrincipalId? SpecialistPrincipal;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var tools = options?.Tools?.OfType<AIFunction>().ToArray() ?? [];
            object? evidence;
            if (messages.Any(message => message.Role == ChatRole.System && message.Text.StartsWith("You are Salesforce,", StringComparison.Ordinal)))
            {
                SpecialistTools = tools.Select(tool => tool.Name).ToArray();
                SpecialistPrincipal = VerifiedActor.Current?.PrincipalId;
                evidence = await tools.Single(tool => tool.Name == "getUserInfo").InvokeAsync([], cancellationToken);
            }
            else
            {
                InoTools = tools.Select(tool => tool.Name).ToArray();
                evidence = await tools.Single(tool => tool.Name == "ask_salesforce")
                    .InvokeAsync(new() { ["request"] = "Who is connected to Salesforce?" }, cancellationToken);
            }
            yield return new ChatResponseUpdate(ChatRole.Assistant, evidence?.ToString() ?? "Missing evidence");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

}
