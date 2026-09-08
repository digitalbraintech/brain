using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[GrainType("llm")]
internal sealed class LlmNeuron(NeuronRuntime runtime, IChatClient chat) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "Ask")
        {
            return Task.CompletedTask;
        }

        ScheduleTurn(ct => CompleteAsk(delivery, ct));
        return Task.CompletedTask;
    }

    private async Task CompleteAsk(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = [];
        var instruct = (await ReadState().ConfigureAwait(true))
            .FirstOrDefault(entry => entry.Signal.Type == "Instruct");
        if (instruct is not null)
        {
            var prompt = SignalText.Read(instruct.Signal.Body);
            if (prompt.Length > 0)
            {
                messages.Add(new(ChatRole.System, prompt));
            }
        }

        messages.Add(new(ChatRole.User, SignalText.Read(delivery.Signal.Body)));
        var response = await chat.GetResponseAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(true);
        await FireAsync(
            Signal.Create("Reply", SignalText.Write(response.Text)),
            delivery.Source,
            delivery.CorrelationId,
            cancellationToken).ConfigureAwait(true);
    }
}
