using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationUiMixedEventTests
{
    [Fact(Timeout = 30000)]
    public async Task Connections_from_one_renderer_filter_each_declared_event_type()
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
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "ui-mixed-events", actor);
        var renderer = brain.Get<IUIRenderer>("desk");
        var app = brain.Application("mixed-observer");
        var observer = app.Behavior("observer");
        var openedCount = observer.State<int>("opened-count", 1);
        var activatedCount = observer.State<int>("activated-count", 1);
        var observedActivation = new TaskCompletionSource<ControlActivated>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = observer.Handle<SurfaceOpened>("opened", (_, run, _) =>
        {
            run.State.Set(openedCount, run.State.Get(openedCount) + 1);
            return Task.CompletedTask;
        });
        var activated = observer.Handle<ControlActivated>("activated", (signal, run, _) =>
        {
            run.State.Set(activatedCount, run.State.Get(activatedCount) + 1);
            observedActivation.TrySetResult(signal);
            return Task.CompletedTask;
        });
        app.Connect("opened", renderer.Events().SurfaceOpened, opened);
        app.Connect("activated", renderer.Events().ControlActivated, activated);
        var readOpened = observer.Command<bool, int>("read-opened", (_, run, _) =>
            Task.FromResult(run.State.Get(openedCount)));
        var readActivated = observer.Command<bool, int>("read-activated", (_, run, _) =>
            Task.FromResult(run.State.Get(activatedCount)));
        using (VerifiedActor.Enter(actor))
        {
            await app.InstallAsync("r1", cancellationToken);
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            await renderer.SendAsync(
                new OpenSurface(CommandId.New(), "home", "Home"), cancellationToken);
            await renderer.SendAsync(
                new ControlActivated("home", "refresh", "refresh"), cancellationToken);
            while (await readOpened.InvokeAsync(true, cancellationToken) != 1
                || await readActivated.InvokeAsync(true, cancellationToken) != 1)
            {
                await Task.Delay(25, cancellationToken);
            }
            var received = await observedActivation.Task.WaitAsync(cancellationToken);
            Assert.Equal("home", received.SurfaceKey);
            Assert.Equal("refresh", received.ControlId);
            Assert.Equal("refresh", received.Intent);
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}
