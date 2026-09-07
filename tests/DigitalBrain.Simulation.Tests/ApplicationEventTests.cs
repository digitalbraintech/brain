using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationEventTests
{
    [Fact]
    public async Task Early_initialization_connection_receives_the_later_root_event_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "early-init", actor);
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
        using (VerifiedActor.Enter(actor)) { await app.InstallAsync("r1", cancellationToken); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            await brain.ActivateAsync(cancellationToken);
            await WaitForCountAsync(read, 1, cancellationToken);
            await brain.ActivateAsync(cancellationToken);
            Assert.Equal(1, await read.InvokeAsync(true, cancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    [Fact]
    public async Task Late_initialization_connection_catches_up_once_for_its_principal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "late-init", actor);
        await brain.ActivateAsync(cancellationToken);

        var app = brain.Application("startup");
        var startup = app.Behavior("startup");
        var count = startup.State<int>("count", 1);
        var initialized = startup.Handle<DigitalBrainActivated>("initialized", (signal, run, _) =>
        {
            if (signal.Owner == brain.Owner)
            {
                run.State.Set(count, run.State.Get(count) + 1);
            }
            return Task.CompletedTask;
        });
        app.Connect("root-initialized", brain.Root.Events.Activated, initialized);
        var read = startup.Command<bool, int>("read", (_, run, _) => Task.FromResult(run.State.Get(count)));
        using (VerifiedActor.Enter(actor)) { await app.InstallAsync("r1", cancellationToken); }

        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
            await WaitForCountAsync(read, 1, cancellationToken);
            await stopping.CancelAsync();
            await worker;
        }
        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
            Assert.Equal(1, await read.InvokeAsync(true, cancellationToken));
            await stopping.CancelAsync();
            await worker;
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Unacked_event_keeps_its_old_recipient_revision_discoverable_and_executes_it_after_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var persistence = Path.Combine(Path.GetTempPath(), "digitalbrain-event-recovery", Guid.NewGuid().ToString("N"));
        var actor = new ActorContext(PrincipalId.New(), "author");
        try
        {
            await using var simulation = await BrainSimulation.StartAsync(new()
            {
                Modules = new ModuleManifest([]),
                PersistenceDirectory = persistence,
            });
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "event-recovery", actor);
            var publisher = brain.Application("publisher");
            var output = publisher.Behavior("source").Event<int>("changed");
            var emit = publisher.Behavior("source").Command<int, int>("emit", async (value, run, ct) =>
                (await run.PublishAsync(output, value, ct)).RecipientCount);
            using (VerifiedActor.Enter(actor)) { await publisher.InstallAsync("publisher-r1", cancellationToken); }

            var oldRecipient = brain.Application("recipient");
            var oldCounter = oldRecipient.Behavior("counter");
            var oldCount = oldCounter.State<int>("count", 1);
            var oldInput = oldCounter.Handle<int>("add", (value, run, _) =>
            {
                run.State.Set(oldCount, run.State.Get(oldCount) + value);
                return Task.CompletedTask;
            });
            oldCounter.Command<bool, int>("read", (_, run, _) => Task.FromResult(run.State.Get(oldCount)));
            using (VerifiedActor.Enter(actor)) { await oldRecipient.InstallAsync("recipient-r1", cancellationToken); }

            var connector = brain.Application("connector");
            connector.Command<bool, bool>("present", (value, _, _) => Task.FromResult(value));
            connector.Connect("publisher-to-recipient", output, oldInput);
            using (VerifiedActor.Enter(actor)) { await connector.InstallAsync("connector-r1", cancellationToken); }

            using (var publisherStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var publisherWorker = simulation.ServeApplicationAsync(publisher, "publisher-r1", publisherStopping.Token);
                Assert.Equal(1, await emit.InvokeAsync(3, cancellationToken));
                await publisherStopping.CancelAsync();
                await publisherWorker;
            }

            var newRecipient = brain.Application("recipient");
            var newCounter = newRecipient.Behavior("counter");
            var newCount = newCounter.State<int>("count", 1);
            newCounter.Handle<int>("add", (value, run, _) =>
            {
                run.State.Set(newCount, run.State.Get(newCount) + (value * 10));
                return Task.CompletedTask;
            });
            newCounter.Command<bool, int>("read", (_, run, _) => Task.FromResult(run.State.Get(newCount)));
            using (VerifiedActor.Enter(actor)) { await newRecipient.InstallAsync("recipient-r2", cancellationToken); }

            await simulation.RestartSiloAsync(cancellationToken);
            await using var recoveredBrain = DigitalBrainClient.Connect(simulation.Grains, "event-recovery", actor);
            var recoveredOld = recoveredBrain.Application("recipient");
            var recoveredOldCounter = recoveredOld.Behavior("counter");
            var recoveredOldCount = recoveredOldCounter.State<int>("count", 1);
            recoveredOldCounter.Handle<int>("add", (value, run, _) =>
            {
                run.State.Set(recoveredOldCount, run.State.Get(recoveredOldCount) + value);
                return Task.CompletedTask;
            });
            recoveredOldCounter.Command<bool, int>("read", (_, run, _) => Task.FromResult(run.State.Get(recoveredOldCount)));
            Assert.Contains("recipient-r1", await recoveredOld.PendingRevisionsAsync(cancellationToken));

            var recoveredNew = recoveredBrain.Application("recipient");
            var recoveredNewCounter = recoveredNew.Behavior("counter");
            var recoveredNewCount = recoveredNewCounter.State<int>("count", 1);
            recoveredNewCounter.Handle<int>("add", (value, run, _) =>
            {
                run.State.Set(recoveredNewCount, run.State.Get(recoveredNewCount) + (value * 10));
                return Task.CompletedTask;
            });
            var recoveredRead = recoveredNewCounter.Command<bool, int>("read", (_, run, _) =>
                Task.FromResult(run.State.Get(recoveredNewCount)));
            using var oldStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var newStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var oldWorker = simulation.ServeApplicationAsync(recoveredOld, "recipient-r1", oldStopping.Token);
            var newWorker = simulation.ServeApplicationAsync(recoveredNew, "recipient-r2", newStopping.Token);
            try
            {
                await WaitForCountAsync(recoveredRead, 3, cancellationToken);
            }
            finally
            {
                await oldStopping.CancelAsync();
                await newStopping.CancelAsync();
                await Task.WhenAll(oldWorker, newWorker);
            }
        }
        finally
        {
            if (Directory.Exists(persistence)) { Directory.Delete(persistence, recursive: true); }
        }
    }

    [Fact]
    public async Task Connection_rejects_an_event_port_from_another_principal_scope()
    {
        await using var simulation = await StartAsync();
        var firstActor = new ActorContext(PrincipalId.New(), "first");
        var secondActor = new ActorContext(PrincipalId.New(), "second");
        await using var first = DigitalBrainClient.Connect(simulation.Grains, "events-scope", firstActor);
        await using var second = DigitalBrainClient.Connect(simulation.Grains, "events-scope", secondActor);
        var foreign = first.Application("shared").Behavior("publisher").Event<int>("changed");
        var localApp = second.Application("shared");
        var local = localApp.Behavior("counter").Handle<int>("add", (_, _, _) => Task.CompletedTask);

        Assert.Throws<InvalidOperationException>(() => localApp.Connect("foreign", foreign, local));
    }

    [Fact]
    public async Task Two_owned_connections_to_the_same_input_create_one_physical_delivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "events-dedup", actor);
        var app = brain.Application("counter");
        var publisher = app.Behavior("publisher");
        var counter = app.Behavior("counter");
        var changed = publisher.Event<int>("changed");
        var input = counter.Handle<int>("add", (_, _, _) => Task.CompletedTask);
        app.Connect("first-owner", changed, input);
        app.Connect("second-owner", changed, input);
        var emit = publisher.Command<int, int>("emit", async (value, run, ct) =>
            (await run.PublishAsync(changed, value, ct)).RecipientCount);

        using (VerifiedActor.Enter(actor)) { await app.InstallAsync("r1", cancellationToken); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            Assert.Equal(1, await emit.InvokeAsync(1, cancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact]
    public async Task Connected_event_updates_durable_handler_state_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "events", actor);
        var app = brain.Application("counter");
        var publisher = app.Behavior("publisher");
        var counter = app.Behavior("counter");
        var count = counter.State<int>("count", 1);
        var changed = publisher.Event<int>("changed");
        var ignored = publisher.Event<int>("ignored");
        var input = counter.Handle<int>("add", (value, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + value);
            return Task.CompletedTask;
        });
        app.Connect("changed-to-counter", changed, input);
        var emit = publisher.Command<int, bool>("emit", async (value, run, ct) =>
        {
            await run.PublishAsync(changed, value, ct);
            return true;
        });
        var emitIgnored = publisher.Command<int, bool>("emit-ignored", async (value, run, ct) =>
        {
            await run.PublishAsync(ignored, value, ct);
            return true;
        });
        var read = counter.Command<bool, int>("read", (request, run, _) =>
            Task.FromResult(run.State.Get(count)));

        using (VerifiedActor.Enter(actor)) { await app.InstallAsync("r1", cancellationToken); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            await emit.InvokeAsync(2, cancellationToken);
            await WaitForCountAsync(read, 2, cancellationToken);

            await emitIgnored.InvokeAsync(100, cancellationToken);
            Assert.Equal(2, await read.InvokeAsync(true, cancellationToken));

            var retryId = Guid.NewGuid();
            await (await emit.SubmitAsync(3, retryId, cancellationToken)).ResultAsync(cancellationToken);
            await (await emit.SubmitAsync(3, retryId, cancellationToken)).ResultAsync(cancellationToken);
            await WaitForCountAsync(read, 5, cancellationToken);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    private static async Task WaitForCountAsync(
        CommandPort<bool, int> read, int expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (await read.InvokeAsync(true, timeout.Token) != expected)
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private static Task<BrainSimulation> StartAsync() => BrainSimulation.StartAsync(new()
    {
        Modules = new ModuleManifest([]),
        Configuration = new Dictionary<string, string?>
        {
            [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
        },
    });
}

