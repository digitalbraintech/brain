using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.AI.Tests;

public sealed class AgentKernelProgrammingInstructionsTests
{
    [Fact(Timeout = 30000)]
    public async Task Real_assistant_kernel_sends_application_authoring_instructions_to_the_model()
    {
        var model = new CapturingChatClient();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model),
        });
        var actor = new ActorContext(PrincipalId.New(), "instruction-user");
        using var verified = VerifiedActor.Enter(actor);
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "instruction-owner", actor);

        var kernel = simulation.Grains.GetGrain<IAgentKernel>(IAgentKernel.IdFor(brain.Owner));
        var response = await kernel.Ask(
            new AgentRequest("Help me program my brain."), CorrelationId.New(),
            TestContext.Current.CancellationToken);

        Assert.Equal("ready", response.Text);
        var instructions = Assert.Single(model.Requests)
            .Where(static message => message.Role == ChatRole.System)
            .Select(static message => message.Text)
            .Aggregate(string.Empty, static (all, message) => all + "\n" + message);
        Assert.Contains("set_application_expectations", instructions, StringComparison.Ordinal);
        Assert.Contains("run_application_scenarios", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not rewrite expectations", instructions, StringComparison.Ordinal);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(messages.ToArray());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ready")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Messages.Single().Text)
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
