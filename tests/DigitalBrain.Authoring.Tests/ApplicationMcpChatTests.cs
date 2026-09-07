using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class ApplicationMcpChatTests
{
    [Fact(Timeout = 30000)]
    public async Task Mcp_receives_authored_chat_reply_and_retries_without_executing_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
        });
        var actor = new ActorContext(new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "mcp-chat", actor);
        var application = brain.Application("ping");
        application.OnUserMessage("ping", "/ping", (_, _, _) => Task.FromResult("pong"));
        await simulation.ApplyApplicationAsync(application, "r1", ct);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var serving = simulation.ServeApplicationAsync(application, "r1", stopping.Token);
        try
        {
            var tools = new ChatTools(brain, simulation.Grains);
            var commandId = Guid.NewGuid().ToString();
            var first = await tools.SendChatMessageAsync("/ping", commandId, timeoutSeconds: 3, cancellationToken: ct);
            var retry = await tools.SendChatMessageAsync("/ping", commandId, timeoutSeconds: 3, cancellationToken: ct);
            Assert.Equal("pong", Assert.IsType<TextContentBlock>(Assert.Single(first.Content)).Text);
            Assert.Equal("pong", Assert.IsType<TextContentBlock>(Assert.Single(retry.Content)).Text);
            var journal = await brain.Get<IComposer>(IComposer.DefaultInstanceName).ReadJournalAsync(JournalKind.Outgoing, 0, ct);
            Assert.Single(journal.Delta, delivery => delivery.Signal is Responded);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }
}
