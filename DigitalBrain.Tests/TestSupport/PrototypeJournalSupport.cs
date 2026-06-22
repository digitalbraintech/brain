using DigitalBrain.Protocol;
using Orleans.Journaling;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journaling - only for tests/prototype

namespace DigitalBrain.Tests.TestSupport;

/// <summary>
/// Prototype in-memory implementations.
/// These exist ONLY for fast harness + unit tests.
/// Real DigitalBrain.Host deployments must provide durable journal + storage.
/// </summary>
// Dual journals support for tests (in + out)
public sealed class InMemoryDurableList<T> : List<T>, IDurableList<T>;

public sealed class TestJournaledStateManager : IJournaledStateManager
{
    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public void RegisterState(string stateId, IJournaledState state) { }
    public bool TryGetState(string stateId, out IJournaledState? state)
    {
        state = null;
        return false;
    }
    public ValueTask WriteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DeleteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
#pragma warning restore ORLEANSEXP005