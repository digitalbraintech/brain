using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationWorkerCapabilityTests
{
    [Fact(Timeout = 30000)]
    public async Task Capability_is_revision_bound_and_remains_valid_after_bootstrap_ticket_expiry()
    {
        var clock = new AdvanceableClock(DateTimeOffset.UtcNow);
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        var actor = new ActorContext(PrincipalId.New(), "worker");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "cap-owner", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var application = brain.Application("cap-app");
        var command = application.Command<string, string>("reply", (_, _, _) => Task.FromResult("pong"));
        await application.InstallAsync("r1", TestContext.Current.CancellationToken);
        await command.SubmitAsync("queued", Guid.NewGuid(), TestContext.Current.CancellationToken);
        await application.InstallAsync("r2", TestContext.Current.CancellationToken);
        var authority = simulation.GetSiloService<ApplicationWorkerCapabilityAuthority>();
        var capability = authority.Issue("cap-owner", actor.PrincipalId.Value, "cap-app", "r1",
            DateTimeOffset.MaxValue);
        var kernel = simulation.Grains.GetGrain<IApplicationKernel>(
            $"cap-owner/{actor.PrincipalId}/cap-app");

        clock.Advance(TimeSpan.FromMinutes(2));
        using (ApplicationWorkerCapabilityContext.Enter(capability))
        {
            Assert.NotNull(await kernel.Claim("r1"));
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() => kernel.Claim("r2"));
        }
        authority.Revoke(capability);
    }

    [Fact(Timeout = 30000)]
    public async Task Revoked_capability_cannot_mutate_an_existing_lease()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "worker");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "cap-owner", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var application = brain.Application("cap-app");
        var command = application.Command<string, string>("reply", (_, _, _) => Task.FromResult("pong"));
        await application.InstallAsync("r1", TestContext.Current.CancellationToken);
        await command.SubmitAsync("queued", Guid.NewGuid(), TestContext.Current.CancellationToken);
        var authority = simulation.GetSiloService<ApplicationWorkerCapabilityAuthority>();
        var capability = authority.Issue("cap-owner", actor.PrincipalId.Value, "cap-app", "r1",
            DateTimeOffset.MaxValue);
        var kernel = simulation.Grains.GetGrain<IApplicationKernel>(
            $"cap-owner/{actor.PrincipalId}/cap-app");
        ApplicationClaim claim;
        using (ApplicationWorkerCapabilityContext.Enter(capability))
        {
            claim = (await kernel.Claim("r1"))!;
        }

        authority.Revoke(capability);
        using (ApplicationWorkerCapabilityContext.Enter(capability))
        {
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
                kernel.Renew(claim.OperationId, claim.Lease));
        }
    }
    private sealed class AdvanceableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
}
