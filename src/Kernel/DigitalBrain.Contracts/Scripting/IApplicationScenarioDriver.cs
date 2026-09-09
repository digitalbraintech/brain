using System.Text.Json;

namespace DigitalBrain.Abstractions.Scripting;

public interface IApplicationScenarioDriver
{
    string Kind { get; }

    Task<string> RunAsync(
        IDigitalBrain brain,
        string applicationKey,
        JsonElement stimulus,
        CancellationToken cancellationToken = default);
}
