using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationUiComponentRecoveryTests
{
    [Fact(Timeout = 30000)]
    public async Task Retrying_a_committed_open_republishes_its_component_receipt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fault = new FailAfterSurfaceCommit();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<IIncomingGrainCallFilter>(fault),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "ui-component-recovery", actor);
        var renderer = brain.Get<IUIRenderer>("desk");
        var command = CommandId.New();
        var open = new OpenSurface(command, "home", "Home",
            new SurfaceComponent("button", "refresh", new Dictionary<string, string>
            {
                ["intent"] = "refresh",
            }));

        var observed = new TaskCompletionSource<ComponentAdded>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = brain.Application("component-recovery-observer");
        var observer = app.Behavior("observer");
        var added = observer.Handle<ComponentAdded>("component-added", (signal, _, _) =>
        {
            observed.TrySetResult(signal);
            return Task.CompletedTask;
        });
        app.Connect("renderer-components", renderer.Events().ComponentAdded, added);
        using (VerifiedActor.Enter(actor))
        {
            await app.InstallAsync("r1", cancellationToken);
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            fault.Arm();
            await Assert.ThrowsAsync<IOException>(() => renderer.SendAsync(open, cancellationToken));
            await renderer.SendAsync(open, cancellationToken);
            var recovered = await observed.Task.WaitAsync(cancellationToken);
            Assert.Equal(command, recovered.CommandId);
            Assert.Equal("refresh", recovered.Component.Key);
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    private sealed class FailAfterSurfaceCommit : IIncomingGrainCallFilter
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            await context.Invoke();
            if (context.InterfaceMethod?.DeclaringType == typeof(ISurface)
                && context.MethodName == nameof(ISurface.Open)
                && Interlocked.Exchange(ref armed, 0) == 1)
            {
                throw new IOException("Injected failure after the surface commit.");
            }
        }
    }
}
