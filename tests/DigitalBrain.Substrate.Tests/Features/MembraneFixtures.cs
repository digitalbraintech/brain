using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.new-post")]
public sealed record NewPost(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.secret")]
public sealed record Secret(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.profile-state")]
public sealed record ProfileState(string Bio);

[Alias("DigitalBrain.Substrate.Tests.IAccount")]
public interface IAccount : INeuron
{
    [Alias(nameof(FireNewPostAt))]
    Task<DeliveryOutcome> FireNewPostAt(string receiverType, string receiverName, string text);

    [Alias(nameof(BroadcastNewPost))]
    Task<int> BroadcastNewPost(string text);

    [Alias(nameof(BroadcastSecret))]
    Task<int> BroadcastSecret(string text);
}

[Alias("DigitalBrain.Substrate.Tests.ITimeline")]
public interface ITimeline : INeuron, IHandle<NewPost>;

[Alias("DigitalBrain.Substrate.Tests.IProfile")]
public interface IProfile : IEntity<ProfileState>
{
    [Alias(nameof(WriteBio))]
    Task WriteBio(string bio);
}

[GrainType("account")]
internal sealed class Account(NeuronRuntime runtime) : Neuron(runtime), IAccount
{
    public async Task<DeliveryOutcome> FireNewPostAt(string receiverType, string receiverName, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiverType);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiverName);
        var result = await SendAsync(
            new NeuronId(receiverType, Id.Owner, receiverName),
            new NewPost(text)).ConfigureAwait(true);
        return result.Outcome;
    }

    public Task<int> BroadcastNewPost(string text) => BroadcastAsync(new NewPost(text));

    public Task<int> BroadcastSecret(string text) => BroadcastAsync(new Secret(text));
}

[GrainType("timeline")]
internal sealed class Timeline(NeuronRuntime runtime) : Neuron(runtime), ITimeline, IHandle<NewPost>
{
    public Task HandleAsync(NewPost signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

[GrainType("profile")]
internal sealed class ProfileEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ProfileState> state)
    : Entity<ProfileState>(state), IProfile
{
    public Task WriteBio(string bio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bio);
        return SaveAsync(new ProfileState(bio.Trim()));
    }
}
