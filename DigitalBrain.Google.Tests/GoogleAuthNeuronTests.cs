using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleAuthNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task AuthRequested_Fires_AuthCompleted()
    {
        var auth = _brain.Grain<IGoogleAuthNeuron>("google-auth-test");
        await auth.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("google-auth-test") });

        var outgoing = await auth.GetTimelineAsync();
        Assert.Contains(outgoing, s => s is Signal reply && reply.Name == GoogleSignals.AuthCompleted);
    }
}
