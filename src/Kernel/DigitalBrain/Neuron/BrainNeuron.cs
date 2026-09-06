using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.BroadcastChannel;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType(IBrainNeuron.GrainTypeName)]
internal sealed class BrainNeuron : Neuron, IBrainNeuron
{
    private const string ActivationPublishedName = "activation-published";

    private readonly IDurableValue<bool> _activationPublished;

    public BrainNeuron(NeuronRuntime runtime)
        : base(runtime)
    {
        _activationPublished = ServiceProvider.GetRequiredKeyedService<IDurableValue<bool>>(ActivationPublishedName);
    }

    public async Task Activate()
    {
        if (_activationPublished.Value)
        {
            return;
        }

        var activated = await RecordOutgoingAsync(new DigitalBrainActivated(Id.Owner))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var writer = ServiceProvider
            .GetRequiredService<IClusterClient>()
            .GetBroadcastChannelProvider(DigitalBrainNames.BroadcastChannelProvider)
            .GetChannelWriter<SignalDelivery>(ChannelId.Create(
                DigitalBrainNames.ActivationChannelNamespace,
                $"{Id.Owner.Value}/{DigitalBrainNames.ActivationSubscriberName}"));
        await writer.Publish(activated)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _activationPublished.Value = true;
        await WriteStateAsync().ConfigureAwait(true);
    }

    public Task<SignalDeliveryResult> Send(NeuronId receiver, Signal signal, CancellationToken cancellationToken = default)
    {
        RequireSameOwner(receiver);
        return SendAsync(receiver, signal, cancellationToken);
    }

    public Task<SignalDeliveryResult> SendWithCorrelation(
        NeuronId receiver,
        Signal signal,
        CorrelationId correlation,
        CancellationToken cancellationToken = default)
    {
        RequireSameOwner(receiver);
        return SendAsync(receiver, signal, correlation, cancellationToken);
    }

    public Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence)
    {
        RequireSameOwner(subject);
        return subject == Id
            ? ReadJournal(kind, afterSequence)
            : GrainFactory.GetGrain<INeuronQuery>(subject.ToGrainId()).ReadJournal(kind, afterSequence);
    }

    public Task<IReadOnlyList<Synapse>> ReadNeuronSynapses(NeuronId subject)
    {
        RequireSameOwner(subject);
        return subject == Id
            ? ReadSynapses()
            : GrainFactory.GetGrain<INeuronQuery>(subject.ToGrainId()).ReadSynapses();
    }

    public Task WatchNeuron(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        IJournalObserver observer)
    {
        RequireSameOwner(subject);
        return subject == Id
            ? Watch(kind, afterSequence, observer)
            : GrainFactory.GetGrain<INeuronQuery>(subject.ToGrainId())
                .Watch(kind, afterSequence, observer);
    }

    public Task UnwatchNeuron(NeuronId subject, IJournalObserver observer)
    {
        RequireSameOwner(subject);
        return subject == Id
            ? Unwatch(observer)
            : GrainFactory.GetGrain<INeuronQuery>(subject.ToGrainId()).Unwatch(observer);
    }

    private void RequireSameOwner(NeuronId subject)
    {
        if (subject.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Owner root '{Id}' cannot access '{subject}', which belongs to owner '{subject.Owner}'.");
        }
    }
}
