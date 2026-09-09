using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationUiEventIngressTests
{
    [Fact]
    public async Task Renderer_open_surface_event_reaches_connected_authored_behavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "ui-events", actor);
        var renderer = brain.Get<IUIRenderer>("desk");
        var app = brain.Application("appearance-observer");
        var observer = app.Behavior("observer");
        var count = observer.State<int>("count", 1);
        var opened = observer.Handle<SurfaceOpened>("surface-opened", (signal, run, _) =>
        {
            Assert.Equal(renderer.Id, signal.Surface);
            run.State.Set(count, run.State.Get(count) + 1);
            return Task.CompletedTask;
        });
        app.Connect("renderer-opened", renderer.Events().SurfaceOpened, opened);
        var read = observer.Command<bool, int>("read", (_, run, _) => Task.FromResult(run.State.Get(count)));
        using (VerifiedActor.Enter(actor)) { await app.InstallAsync("r1", cancellationToken); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            var command = CommandId.New();
            await renderer.SendAsync(new OpenSurface(command, "home", "Home"), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (await read.InvokeAsync(true, timeout.Token) != 1)
            {
                await Task.Delay(25, timeout.Token);
            }
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}

