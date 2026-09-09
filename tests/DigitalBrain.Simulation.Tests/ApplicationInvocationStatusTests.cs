using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationInvocationStatusTests
{
    [Fact(Timeout = 30000)]
    public async Task Retrying_after_a_new_revision_returns_the_original_result_and_identity()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "invocation-status",
            new ActorContext(PrincipalId.New(), "author"));
        var original = brain.Application("greeting");
        original.Command<string, string>("reply", (_, _, _) => Task.FromResult("original"));
        await original.InstallAsync("one", TestContext.Current.CancellationToken);
        var authoring = new ApplicationAuthoringService(Path.GetTempPath());
        var id = Guid.NewGuid();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(original, "one", stopping.Token);
        try
        {
            var first = await authoring.InvokeAsync(brain, "greeting", "reply", "\"hello\"", id,
                TestContext.Current.CancellationToken);
            Assert.Equal("\"original\"", first.Value);
            var replacement = brain.Application("greeting");
            replacement.Command<string, string>("reply", (_, _, _) => Task.FromResult("replacement"));
            await replacement.InstallAsync("two", TestContext.Current.CancellationToken);
            var retry = await authoring.InvokeAsync(brain, "greeting", "reply", "\"hello\"", id,
                TestContext.Current.CancellationToken);
            Assert.Equal(first, retry);
            Assert.Equal("one", retry.Revision);
            Assert.Equal(retry, await authoring.ReadInvocationAsync(brain, "greeting", id,
                TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => authoring.InvokeAsync(
                brain, "greeting", "reply", "\"different\"", id, TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }
}

