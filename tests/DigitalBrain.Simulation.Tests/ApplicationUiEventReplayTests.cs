using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationUiEventReplayTests
{
    [Fact(Timeout = 30000)]
    public async Task Default_renderer_connection_starts_after_its_installation_cursor()
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
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "ui-event-cursor", actor);
        var renderer = brain.Get<IUIRenderer>("desk");
        var historical = CommandId.New();
        await renderer.SendAsync(new OpenSurface(historical, "old", "Old"), cancellationToken);

        var observed = new TaskCompletionSource<SurfaceOpened>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = brain.Application("appearance-observer");
        var observer = app.Behavior("observer");
        var input = observer.Handle<SurfaceOpened>("surface-opened", (signal, _, _) =>
        {
            observed.TrySetResult(signal);
            return Task.CompletedTask;
        });
        app.Connect("renderer-opened", renderer.Events().SurfaceOpened, input);
        using (VerifiedActor.Enter(actor)) { await app.InstallAsync("r1", cancellationToken); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            var current = CommandId.New();
            await renderer.SendAsync(new OpenSurface(current, "new", "New"), cancellationToken);
            var received = await observed.Task.WaitAsync(cancellationToken);
            Assert.Equal(current, received.CommandId);
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}
