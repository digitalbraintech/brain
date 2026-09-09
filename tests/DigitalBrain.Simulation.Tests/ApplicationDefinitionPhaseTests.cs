using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationDefinitionPhaseTests
{
    [Fact(Timeout = 30000)]
    public async Task Definition_cannot_submit_an_operation_to_an_existing_application()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "definition",
            new ActorContext(PrincipalId.New(), "author"));
        var existing = brain.Application("existing");
        var action = existing.Command<string, string>("action", (_, _, _) => Task.FromResult("done"));
        await existing.InstallAsync("existing-one", TestContext.Current.CancellationToken);

        using var definition = ApplicationRunScope.Enter(ApplicationRunMode.Install, "candidate-one", "candidate",
            TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.SubmitAsync("must not run", TestContext.Current.CancellationToken));
        Assert.Contains("definition", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await existing.PendingRevisionsAsync(TestContext.Current.CancellationToken));
    }
}

