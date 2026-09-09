using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationStateTests : IDisposable
{
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "digitalbrain-state-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 30000)]
    public async Task Increment_then_call_then_worker_restart_does_not_increment_twice()
    {
        var clock = new RecoveryClock();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = _storage,
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "stateful",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("counter");
        var child = app.Command<int, int>("echo", (value, _, _) => Task.FromResult(value));
        var behavior = app.Behavior("counter");
        var count = behavior.State<int>("count", schemaVersion: 1);
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var increment = behavior.Command<string, int>("increment", async (_, run, ct) =>
        {
            run.State.Set(count, run.State.Get(count) + 1);
            await run.InvokeAsync(child, run.State.Get(count), ct);
            checkpoint.TrySetResult();
            await resume.Task.WaitAsync(ct);
            return run.State.Get(count);
        });
        await app.InstallAsync("counter-one", TestContext.Current.CancellationToken);
        var first = await increment.SubmitAsync("", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "counter-one", stopping.Token);
        await checkpoint.Task.WaitAsync(TestContext.Current.CancellationToken);
        await stopping.CancelAsync();
        await worker;
        await simulation.RestartSiloAsync(TestContext.Current.CancellationToken);
        clock.Advance();
        resume.SetResult();
        using var recoveredStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var recovered = simulation.ServeApplicationAsync(app, "counter-one", recoveredStopping.Token);
        try
        {
            Assert.Equal(1, await first.ResultAsync(recoveredStopping.Token));
            Assert.Equal(2, await increment.InvokeAsync("", recoveredStopping.Token));
        }
        finally
        {
            await recoveredStopping.CancelAsync();
            await recovered;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_storage)) { Directory.Delete(_storage, recursive: true); }
    }

    private sealed class RecoveryClock : TimeProvider
    {
        private long _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offset));
        public void Advance() => Interlocked.Add(ref _offset, TimeSpan.FromSeconds(31).Ticks);
    }
}

