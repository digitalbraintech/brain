using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Tests;

[GenerateSerializer]
[Alias("db.test.profile-state")]
public sealed record ProfileState([property: Id(0)] string Bio);

[Alias("db.test.profile")]
public interface IProfile : IGrainWithStringKey
{
    [Alias(nameof(ReadBio))]
    Task<string?> ReadBio();
}

// A Neuron<TState>: reacts to "SetBio" {"bio":"..."} by saving a snapshot. Everything else is ignored.
[GrainType("profile")]
internal sealed class Profile(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ProfileState> state)
    : Neuron<ProfileState>(runtime, state), IProfile
{
    public Task<string?> ReadBio() => Task.FromResult(State?.Bio);

    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "SetBio")
        {
            return Task.CompletedTask;
        }

        using var body = JsonDocument.Parse(delivery.Signal.Body);
        return SaveAsync(new ProfileState(body.RootElement.GetProperty("bio").GetString() ?? ""), cancellationToken);
    }
}
