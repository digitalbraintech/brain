using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationWorkerAuthorizationTests
{
    [Fact(Timeout = 30000)]
    public async Task Same_principal_cannot_claim_a_revision_without_a_server_worker_capability()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "application-author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "capability-owner", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var application = brain.Application("protected-app");
        var command = application.Command<string, string>(
            "reply", (_, _, _) => Task.FromResult("must-not-run"));
        await application.InstallAsync("protected-revision", TestContext.Current.CancellationToken);
        await command.SubmitAsync("queued", Guid.NewGuid(), TestContext.Current.CancellationToken);

        // This client has the same principal identity and knows the immutable revision, but it
        // was never granted the server-issued worker capability for this application process.
        var kernel = simulation.Grains.GetGrain<IApplicationKernel>(
            $"capability-owner/{actor.PrincipalId}/protected-app");
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            kernel.Claim("protected-revision"));
    }
}

