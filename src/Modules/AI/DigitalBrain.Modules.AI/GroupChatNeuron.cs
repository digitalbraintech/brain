using System.Text.Json;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[GrainType("groupchat")]
internal sealed class GroupChatNeuron(NeuronRuntime runtime, IChatClient chat) : Neuron(runtime)
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
        var participants = await ParticipantsAsync().ConfigureAwait(true);
        if (participants.Count < 2)
        {
            await FireAsync(
                Signal.Create("Reply", SignalText.Write(
                    "Instruct this groupchat with at least two participants before Ask.")),
                delivery.Source,
                delivery.CorrelationId,
                cancellationToken).ConfigureAwait(true);
            return;
        }

        var agents = participants
            .Select(participant => (AIAgent)new ChatClientAgent(
                chat,
                participant.Instructions,
                participant.Name,
                participant.Name))
            .ToArray();
        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(team => new RoundRobinGroupChatManager(team)
            {
                MaximumIterationCount = Math.Max(2, agents.Length * 2),
            })
            .AddParticipants(agents)
            .Build();

        List<ChatMessage> input = [new(ChatRole.User, SignalText.Read(delivery.Signal.Body))];
        await using var run = await InProcessExecution.Lockstep
            .RunStreamingAsync(workflow, input, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(true);

        var reply = "";
        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (evt is AgentResponseUpdateEvent update)
            {
                var text = update.AsResponse().Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var author = string.IsNullOrWhiteSpace(update.ExecutorId) ? "participant" : update.ExecutorId;
                await FireAsync(
                    Signal.Create("Said", SignalText.Said(author, text)),
                    delivery.Source,
                    delivery.CorrelationId,
                    cancellationToken).ConfigureAwait(true);
                reply = text;
            }
            else if (evt is WorkflowOutputEvent output && output.Data is IEnumerable<ChatMessage> history)
            {
                var last = history.LastOrDefault(message => message.Role == ChatRole.Assistant);
                if (last is not null && !string.IsNullOrWhiteSpace(last.Text))
                {
                    reply = last.Text;
                }
            }
        }

        await FireAsync(
            Signal.Create("Reply", SignalText.Write(reply)),
            delivery.Source,
            delivery.CorrelationId,
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<IReadOnlyList<Participant>> ParticipantsAsync()
    {
        var instruct = (await ReadState().ConfigureAwait(true))
            .FirstOrDefault(entry => entry.Signal.Type == "Instruct");
        if (instruct is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(instruct.Signal.Body);
        if (!document.RootElement.TryGetProperty("participants", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Select(static item => new Participant(
                item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                item.TryGetProperty("instructions", out var instructions) ? instructions.GetString() ?? "" : ""))
            .Where(static participant => participant.Name.Length > 0)];
    }

    private readonly record struct Participant(string Name, string Instructions);
}
