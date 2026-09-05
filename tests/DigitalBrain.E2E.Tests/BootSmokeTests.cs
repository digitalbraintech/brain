using System.Net;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// The first live run of the whole BrainAppHostFixture pipeline -- a real AppHost boot
// (Azurite, silos, kernel, mcp) driving the facade across process boundaries, unlike the
// in-process simulation DigitalBrainTests.
[Collection(E2ECollection.Name)]
public sealed class BootSmokeTests(AppHostFixture fixture)
{
    [Fact]
    public async Task KernelServesHealth()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void TestHostDoesNotClaimTheDevelopmentKernelPort()
    {
        using var http = fixture.CreateHttpClient("kernel");
        Assert.NotNull(http.BaseAddress);
        Assert.NotEqual(5080, http.BaseAddress.Port);
    }

    [Fact]
    public async Task FacadeSendsAcrossProcessesAndJournals()
    {
        var brain = fixture.BrainFor($"e2e-{Guid.NewGuid().ToString("N")[..8]}");
        await brain.ActivateAsync(TestContext.Current.CancellationToken);

        // Observe the activation's journal footprint using the same subject/kind the simulation
        // suite's JournalSmokeTests.ActivationLandsInTheBrainJournal pins: DigitalBrainActivated
        // lands in the owner brain root's own Outgoing journal.
        var delivery = await JournalWait.ForAsync(
            brain,
            JournalKind.Outgoing,
            static delivery => delivery.Signal is DigitalBrainActivated,
            TimeSpan.FromSeconds(60),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<DigitalBrainActivated>(delivery.Signal);
    }
}
