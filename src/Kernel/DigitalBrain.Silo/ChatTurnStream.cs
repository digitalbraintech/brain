using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Product.Identity;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

// Both typed and voice chat observe the same durable turn. Closing an HTTP request
// detaches its watch; it does not cancel the turn or erase its result.
internal static class ChatTurnStream
{
    internal static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);

    public static async IAsyncEnumerable<SseItem<object>> SendAsync(
        IDigitalBrain brain,
        IWorkspaceInject inject,
        string workspaceName,
        string text,
        ActorContext actor,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var budget = new CancellationTokenSource(TurnBudget);
        var composer = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var before = await composer.ReadJournalAsync(JournalKind.Outgoing, long.MaxValue, budget.Token)
            .ConfigureAwait(false);
        var command = await inject.InjectUserMessage(workspaceName, text, actor, budget.Token)
            .ConfigureAwait(false);
        yield return new SseItem<object>(
            new ChatStreamAccepted(command.ToString(), command.ToString()),
            HttpSurfacePaths.ChatAcceptedEvent);

        using var observer = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, budget.Token);
        await using var pages = composer.WatchJournalAsync(JournalKind.Outgoing, before.ResumeSequence, observer.Token)
            .GetAsyncEnumerator(observer.Token);
        while (true)
        {
            var timedOut = false;
            var hasPage = false;
            try
            {
                hasPage = await pages.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested && budget.IsCancellationRequested)
            {
                timedOut = true;
            }

            if (timedOut || !hasPage)
            {
                yield return Error(new ChatStreamError(
                    "The response stream ended before an answer arrived. Check Activity for the request status.",
                    timedOut ? "TimedOut" : "Disconnected", command.ToString(), command.ToString()));
                yield break;
            }

            if (pages.Current.ResetSnapshot is not null)
            {
                yield return Error(new ChatStreamError(
                    "The live response could not be recovered after a gap in activity. Check Activity for the request status.",
                    "Disconnected", command.ToString(), command.ToString()));
                yield break;
            }

            foreach (var delivery in pages.Current.Delta)
            {
                if (delivery.Signal is Responded responded && responded.CommandId == command)
                {
                    yield return new SseItem<object>(
                        new ChatResponseUpdate(ChatRole.Assistant, responded.Text), HttpSurfacePaths.ChatDeltaEvent);
                    yield break;
                }

                if (delivery.Signal is TurnLifecycle life
                    && life.CommandId == command
                    && life.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled or ChatTurnStatus.Completed)
                {
                    yield return Error(ForTerminal(life));
                    yield break;
                }
            }
        }
    }

    internal static ChatStreamError ForTerminal(TurnLifecycle life)
        => new(
            life.Status switch
            {
                ChatTurnStatus.Cancelled => "This request was cancelled. Send a new message to try again.",
                ChatTurnStatus.Completed => "The assistant finished without an answer. Try sending the message again.",
                _ => "The assistant could not finish this request. Check Activity for details, then try again.",
            },
            life.Status.ToString(), life.TurnId.ToString(), life.CommandId.ToString());

    private static SseItem<object> Error(ChatStreamError error) => new(error, HttpSurfacePaths.ChatErrorEvent);
}

internal sealed record ChatStreamError(string Message, string Status, string TurnId, string CommandId);
internal sealed record ChatStreamAccepted(string CommandId, string TurnId);
