using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationUiComponentEventTests
{
    [Fact(Timeout = 30000)]
    public async Task Committed_component_additions_reach_authored_handlers_once()
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
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "ui-components", actor);
        var renderer = brain.Get<IUIRenderer>("desk");
        var app = brain.Application("component-observer");
        var observer = app.Behavior("observer");
        var count = observer.State<int>("count", 1);
        var lastKey = observer.State<string>("last-key", 1);
        var added = observer.Handle<ComponentAdded>("component-added", (signal, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + 1);
            run.State.Set(lastKey, signal.Component.Key ?? "");
            return Task.CompletedTask;
        });
        app.Connect("renderer-components", renderer.Events().ComponentAdded, added);
        var readCount = observer.Command<bool, int>("read-count", (_, run, _) =>
            Task.FromResult(run.State.Get(count)));
        var readLastKey = observer.Command<bool, string>("read-last-key", (_, run, _) =>
            Task.FromResult(run.State.Get(lastKey)));
        using (VerifiedActor.Enter(actor))
        {
            await app.InstallAsync("r1", cancellationToken);
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            var refresh = Button("refresh");
            await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "Home", refresh), cancellationToken);
            await AwaitCountAsync(readCount, 1, cancellationToken);
            Assert.Equal("refresh", await readLastKey.InvokeAsync(true, cancellationToken));

            await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "Home", refresh), cancellationToken);
            var root = new SurfaceComponent("column", Children: [refresh, Button("save")]);
            await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "Home", root), cancellationToken);
            await AwaitValueAsync(readLastKey, "save", cancellationToken);
            Assert.Equal(2, await readCount.InvokeAsync(true, cancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    private static SurfaceComponent Button(string key)
        => new("button", key, new Dictionary<string, string> { ["intent"] = key });

    private static async Task AwaitCountAsync(
        CommandPort<bool, int> read,
        int expected,
        CancellationToken cancellationToken)
    {
        while (await read.InvokeAsync(true, cancellationToken) != expected)
        {
            await Task.Delay(25, cancellationToken);
        }
    }

    private static async Task AwaitValueAsync(
        CommandPort<bool, string> read,
        string expected,
        CancellationToken cancellationToken)
    {
        while (await read.InvokeAsync(true, cancellationToken) != expected)
        {
            await Task.Delay(25, cancellationToken);
        }
    }
}
