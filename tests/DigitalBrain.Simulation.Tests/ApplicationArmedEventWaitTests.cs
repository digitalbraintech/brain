using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationArmedEventWaitTests
{
    [Fact(Timeout = 30000)]
    public async Task Armed_wait_observes_an_immediate_event_from_the_following_durable_effect()
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
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "armed-event-wait", actor);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        var application = brain.Application("surface-command");
        var open = application.Command<string, string>("open", async (componentKey, run, token) =>
        {
            var response = await run.BeginWaitAsync(renderer.Events().ComponentAdded, token);
            var component = new SurfaceComponent("button", componentKey,
                new Dictionary<string, string> { ["intent"] = "save" });
            await run.SendAsync(renderer,
                new OpenSurface(new CommandId(run.OperationId), "home", "Home", component), token);
            return (await response.ResultAsync(token)).Component.Key ?? "";
        });
        using (VerifiedActor.Enter(actor))
        {
            await application.InstallAsync("r1", ct);
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var worker = simulation.ServeApplicationAsync(application, "r1", stopping.Token);
        try
        {
            Assert.Equal("save-button", await open.InvokeAsync("save-button", ct));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}
