using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationEventToNeuronTests
{
    [Fact(Timeout = 30000)]
    public async Task Authored_application_event_reaches_explicit_neuron_input_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "app-event-neuron", actor);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        var application = brain.Application("activity-publisher");
        var behavior = application.Behavior("activity");
        var changed = behavior.Event<ActivityChanged>("changed");
        application.Connect("activity-to-renderer", changed, renderer.Inputs().ActivityChanged);
        var publish = behavior.Command<string, bool>("publish", async (id, run, token) =>
        {
            var now = DateTimeOffset.UnixEpoch;
            await run.PublishAsync(changed, new ActivityChanged(new ActivityView(
                id, "correlation", "root-signal", "scenario", "Published activity", "running",
                now, now, [], [], Principal: actor.PrincipalId)), token);
            return true;
        });
        using (VerifiedActor.Enter(actor))
        {
            await application.InstallAsync("r1", ct);
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var worker = simulation.ServeApplicationAsync(application, "r1", stopping.Token);
        try
        {
            var operationId = Guid.NewGuid();
            Assert.True(await (await publish.SubmitAsync("activity-1", operationId, ct)).ResultAsync(ct));
            Assert.True(await (await publish.SubmitAsync("activity-1", operationId, ct)).ResultAsync(ct));
            await WaitUntilAsync(async () => (await renderer.ReadJournalAsync(JournalKind.Outgoing, 0, ct)).Delta
                .Count(item => item.Principal == actor.PrincipalId
                    && item.Signal is ActivityChanged update
                    && update.Activity.Id == "activity-1") == 1, ct);
            var delivered = Assert.Single((await renderer.ReadJournalAsync(JournalKind.Outgoing, 0, ct)).Delta,
                item => item.Signal is ActivityChanged update && update.Activity.Id == "activity-1");
            Assert.Equal(new CorrelationId(operationId), delivered.CorrelationId);
            Assert.Equal(actor.PrincipalId, delivered.Principal);
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }
}
