using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationWorkerDiscoveryTests
{
    [Fact(Timeout = 30000)]
    public async Task Supervisor_can_find_old_revisions_until_their_inputs_finish()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "workers",
            new ActorContext(PrincipalId.New(), "author"));
        var old = brain.Application("greeting");
        var reply = old.Command<string, string>("reply", (_, _, _) => Task.FromResult("old response"));
        await old.InstallAsync("old", TestContext.Current.CancellationToken);
        var admitted = await reply.SubmitAsync("hello", TestContext.Current.CancellationToken);
        var current = brain.Application("greeting");
        current.Command<string, string>("reply", (_, _, _) => Task.FromResult("new response"));
        await current.InstallAsync("new", TestContext.Current.CancellationToken);
        Assert.Equal(["old"], await current.PendingRevisionsAsync(TestContext.Current.CancellationToken));
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = simulation.ServeApplicationAsync(old, "old", stopping.Token);
        try
        {
            Assert.Equal("old response", await admitted.ResultAsync(stopping.Token));
            Assert.Empty(await current.PendingRevisionsAsync(stopping.Token));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}

