using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

[GrainType("xaccount")]
internal sealed class XAccountNeuron(NeuronRuntime runtime) : Neuron(runtime), IXAccount
{
    public async Task HandleAsync(PublishPost signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        await RecordOutgoingAsync(new NewPost(signal.Text))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await BroadcastAsync(new NewPost(signal.Text)).ConfigureAwait(true);
    }
}
