using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationEventWaitRecoveryTests : IDisposable
{
    private readonly string persistence = Path.Combine(
        Path.GetTempPath(), "db-event-wait", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Registered_event_wait_receives_event_published_while_worker_is_stopped_after_silo_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            PersistenceDirectory = persistence,
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "event-wait-owner", actor);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        var application = brain.Application("event-waiter");
        var wait = application.Command<string, string>("wait", async (_, run, ct) =>
            (await run.WaitForAsync(renderer.Events().SurfaceOpened, ct)).Title);
        await application.InstallAsync("r1", cancellationToken);
        var invocation = await wait.SubmitAsync("next", Guid.NewGuid(), cancellationToken);
        using (var firstStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var firstWorker = simulation.ServeApplicationAsync(application, "r1", firstStopping.Token);
            await WaitUntilAsync(async () =>
                (await application.ReadInvocationAsync(invocation.OperationId, cancellationToken)).Status == "waiting",
                cancellationToken);
            await firstStopping.CancelAsync();
            await firstWorker;
        }

        await renderer.SendAsync(new OpenSurface(CommandId.New(), "arrived", "Arrived"), cancellationToken);
        await simulation.RestartSiloAsync(cancellationToken);
        await using var recoveredBrain = DigitalBrainClient.Connect(simulation.Grains, "event-wait-owner", actor);
        var recoveredRenderer = recoveredBrain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        var recovered = recoveredBrain.Application("event-waiter");
        var recoveredWait = recovered.Command<string, string>("wait", async (_, run, ct) =>
            (await run.WaitForAsync(recoveredRenderer.Events().SurfaceOpened, ct)).Title);
        var recoveredInvocation = await recoveredWait.SubmitAsync(
            "next", invocation.OperationId, cancellationToken);
        using var recoveredStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var recoveredWorker = simulation.ServeApplicationAsync(recovered, "r1", recoveredStopping.Token);
        try
        {
            Assert.Equal("Arrived", await recoveredInvocation.ResultAsync(cancellationToken));
        }
        finally
        {
            await recoveredStopping.CancelAsync();
            await recoveredWorker;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(persistence)) { Directory.Delete(persistence, recursive: true); }
    }
}
