using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class MissingChatRouteTests
{
    private static readonly ActorContext Owner = new(
        new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")),
        "owner");

    [Fact(Timeout = 10000)]
    public async Task Unmatched_message_with_no_subscribers_records_terminal_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await StartAsync();
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "missing-route", Owner);
        await brain.ActivateAsync(ct);

        CommandId command;
        using (VerifiedActor.Enter(Owner))
        {
            command = await new WorkspaceInject(brain, simulation.Grains)
                .InjectUserMessage("main", "/has-no-route", Owner, ct);
        }

        var composer = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var failure = await WaitForFailureAsync(composer, command, ct);

        Assert.Equal(ChatTurnStatus.Failed, failure.Status);
        Assert.Contains("recipient", failure.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 10000)]
    public async Task Mcp_chat_surfaces_missing_route_failure_without_waiting_for_timeout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await StartAsync();
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "missing-route-mcp", Owner);
        var tools = new ChatTools(brain, simulation.Grains);

        var timer = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tools.SendChatMessageAsync(
                "/has-no-route",
                Guid.NewGuid().ToString(),
                timeoutSeconds: 5,
                cancellationToken: ct));

        Assert.Contains("recipient", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2),
            $"Terminal failure was not surfaced immediately ({timer.Elapsed}).");
    }

    private static Task<BrainSimulation> StartAsync()
        => BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });

    private static async Task<TurnLifecycle> WaitForFailureAsync(
        NeuronReference<IComposer> composer,
        CommandId command,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await foreach (var page in composer.WatchJournalAsync(
            JournalKind.Outgoing, 0, timeout.Token))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Signal is TurnLifecycle lifecycle
                    && lifecycle.CommandId == command
                    && lifecycle.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
                {
                    return lifecycle;
                }
            }
        }

        throw new InvalidOperationException("The composer journal ended without a terminal turn.");
    }
}
