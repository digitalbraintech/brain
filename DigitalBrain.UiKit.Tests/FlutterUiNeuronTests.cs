using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;
using Xunit;

namespace DigitalBrain.UiKit.Tests;

// Closes a pre-existing gap: IFlutterUiNeuron had no direct test before this plan — it was
// only exercised indirectly inside a Telegram test.
public class FlutterUiNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task HandleAsync_Records_The_Delivered_UiSurface_In_The_Incoming_Journal()
    {
        var flutter = _brain.Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = new UiSurface("test-kind", new Dictionary<string, object?> { ["title"] = "smoke" });

        // DeliverAsync (not HandleAsync directly) is the real entry point every production caller uses
        // (see DataVisualizationNeuron) — it's what records the incoming journal before dispatching to
        // FlutterUiNeuron.HandleAsync via the declared IHandle<UiSurface> interface.
        await flutter.DeliverAsync(surface);

        var incoming = await flutter.GetIncomingTimelineAsync();
        Assert.Contains(incoming, s => s is UiSurface delivered && delivered.Kind == "test-kind");
    }
}
