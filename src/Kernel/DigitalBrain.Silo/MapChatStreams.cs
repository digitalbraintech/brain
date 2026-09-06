using DigitalBrain.Product.Identity;
using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Chat;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Kernel;

internal static class ChatStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapChatStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.ChatEventsPath,
            static async Task (
                HttpContext http,
                string chatName,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                var actor = HttpActor.Current;

                if (string.IsNullOrWhiteSpace(chatName)
                    || !TryPrincipalResource(actor.PrincipalId, chatName, out var chatInstance))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchChatTurnsAsync(sessionJournal, chatInstance, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static bool TryPrincipalResource(PrincipalId principal, string localName, out string instanceName)
    {
        try
        {
            instanceName = PrincipalScoped.InstanceName(principal, localName);
            return true;
        }
        catch (ArgumentException)
        {
            instanceName = "";
            return false;
        }
    }

    private static IAsyncEnumerable<SseItem<ChatTurnEvent>> WatchChatTurnsAsync(
        OwnerSessionJournal sessionJournal,
        string chatInstance,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchChatOutgoingAsync(chatInstance, afterSequence, token),
            HttpSurfacePaths.ChatTurnEvent,
            ProjectTurn,
            cancellationToken);

    private static ChatTurnEvent? ProjectTurn(SignalDelivery delivery)
    {
        ChatTurnEvent Turn(
            bool fromUser,
            string text,
            CommandId command,
            string signalName,
            NeuronId chat,
            string? turnId = null,
            string? status = null,
            KitCardOffer[]? cards = null,
            UserActionRequest? userAction = null)
            => new(
                delivery.Sequence,
                fromUser,
                text,
                command.ToString(),
                signalName,
                chat.ToString(),
                delivery.Caller.ToString(),
                delivery.CorrelationId.ToString(),
                delivery.Timestamp,
                turnId,
                status,
                cards,
                userAction,
                delivery.SignalId.ToString());

        return delivery.Signal switch
        {
            UserMessaged messaged =>
                Turn(true, messaged.Text, messaged.CommandId, nameof(UserMessaged), messaged.Chat),
            Responded responded =>
                Turn(
                    false,
                    responded.Text,
                    responded.CommandId,
                    nameof(Responded),
                    responded.Chat,
                    turnId: responded.TurnId?.ToString(),
                    status: responded.UserAction is null ? null : nameof(ChatTurnStatus.WaitingForUser),
                    cards: responded.Cards,
                    userAction: responded.UserAction),
            TurnLifecycle life =>
                Turn(
                    false,
                    life.Detail ?? life.Status.ToString(),
                    life.CommandId,
                    nameof(TurnLifecycle),
                    life.Chat,
                    turnId: life.TurnId.ToString(),
                    status: life.Status.ToString()),
            _ => null,
        };
    }
}
