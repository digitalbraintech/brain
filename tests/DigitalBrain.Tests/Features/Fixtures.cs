using System.Collections.Concurrent;
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

// Replies Pong{n} to the source of every Ping{n}, from inside ReceiveAsync.
[GrainType("echo")]
internal sealed class EchoNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => delivery.Signal.Type == "Ping"
            ? FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken)
            : Task.CompletedTask;
}

// Test switches shared across activations of fixture grains in one silo.
public static class FixtureSwitches
{
    public static ConcurrentDictionary<string, int> FlakyFailuresLeft { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, int> Reactions { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, bool> Asleep { get; } = new(StringComparer.Ordinal);
}

// Throws on the first reaction to each entry while FlakyFailuresLeft[name] > 0, then echoes.
[GrainType("flaky")]
internal sealed class FlakyNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var key = $"{Id.Name}:{delivery.Sequence}";
        FixtureSwitches.Reactions.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (FixtureSwitches.FlakyFailuresLeft.AddOrUpdate(Id.Name, 0, (_, left) => left - 1) >= 0)
        {
            throw new InvalidOperationException("flaky: first reaction fails");
        }

        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken);
    }
}

// While Asleep[name] is true the reaction throws (so the cursor stays); when awake it echoes.
[GrainType("sleepy")]
internal sealed class SleepyNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (FixtureSwitches.Asleep.TryGetValue(Id.Name, out var asleep) && asleep)
        {
            throw new InvalidOperationException("sleepy: not yet");
        }

        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken);
    }
}
