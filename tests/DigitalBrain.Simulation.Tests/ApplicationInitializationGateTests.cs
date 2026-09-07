using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationInitializationGateTests
{
    [Fact(Timeout = 30000)]
    public async Task Applying_a_late_subscription_without_configuration_awaits_its_initialization_handler()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "late-apply",
            new ActorContext(PrincipalId.New(), "author"));
        await brain.ActivateAsync(ct);
        var initialized = false;
        var app = brain.Application("late");
        var input = app.Behavior("startup").Handle<DigitalBrainActivated>("initialized", (_, _, _) =>
        {
            initialized = true;
            return Task.CompletedTask;
        });
        app.Connect("root-initialized", brain.Root.Events.Activated, input);

        await simulation.ApplyApplicationAsync(app, "r1", ct);

        Assert.True(initialized);
    }

    [Fact]
    public async Task OnApply_initializes_only_after_configuration_and_admits_the_typed_root_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "apply-init", actor);
        var app = brain.Application("startup");
        var startup = app.Behavior("startup");
        var count = startup.State<int>("count", 1);
        var initialized = startup.Handle<DigitalBrainActivated>("initialized", (_, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + 1);
            return Task.CompletedTask;
        });
        app.Connect("root-initialized", brain.Root.Events.Activated, initialized);
        var read = startup.Command<bool, int>("read", (_, run, _) => Task.FromResult(run.State.Get(count)));
        var configured = false;
        app.OnApply(async (run, ct) =>
        {
            Assert.DoesNotContain((await brain.ReadJournalAsync(JournalKind.Outgoing, 0, ct)).Delta,
                entry => entry.Signal is DigitalBrainActivated);
            configured = true;
            await run.EnsureInitializedAsync(ct);
        });

        using (VerifiedActor.Enter(actor)) { await simulation.ApplyApplicationAsync(app, "r1", cancellationToken); }
        Assert.True(configured);
        Assert.Single((await brain.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta,
            entry => entry.Signal is DigitalBrainActivated);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
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

