using DigitalBrain.Product.Identity;
using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class ChatTools(IDigitalBrain brain, IGrainFactory grains)
{
    private const int DefaultTimeoutSeconds = 300;
    private const int MaximumTimeoutSeconds = 300;

    // Same single-owner principal as kernel HTTP (HttpActor). Inbox, workspace
    // correlation, behaviors, and kit entities must share this partition.
    private static readonly ActorContext OwnerActor = new(
        new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")),
        "owner");

    [McpServerTool(Name = McpSurface.SendChatMessage)]
    [Description("Send a message to the owner workspace and await its correlated reply from an authored behavior or the assistant. Retry with the same text, commandId and chatName to recover a retained reply. If login is required, complete it in the browser. Never send credentials in chat.")]
    public async Task<CallToolResult> SendChatMessageAsync(
        [Description("Message to send to DigitalBrain")] string text,
        [Description("Caller-generated command id used to resume an interrupted call")]
        string commandId,
        [Description("Workspace name, for example 'main'")] string chatName = "main",
        [Description("Maximum wait in seconds, from 1 through 300")]
        int timeoutSeconds = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default,
        McpServer? server = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutSeconds, MaximumTimeoutSeconds);

        if (!Guid.TryParse(commandId, out var commandIdentity) || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException("The command id must be a non-empty GUID.", nameof(commandId));
        }

        var command = new CommandId(commandIdentity);

        using var actor = VerifiedActor.Enter(OwnerActor);
        await brain.ActivateAsync(cancellationToken).ConfigureAwait(false);
        var composer = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var before = await composer.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)
            .ConfigureAwait(false);
        await InjectAsync(chatName, text, command, cancellationToken).ConfigureAwait(false);

        foreach (var delivery in before.Delta)
        {
            if (ResultFromDelivery(delivery, command, server, includeUserAction: false) is { } retained)
            {
                return retained;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(composer, command, before.ResumeSequence, server, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in workspace "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    private async Task InjectAsync(
        string workspaceName,
        string text,
        CommandId command,
        CancellationToken cancellationToken)
    {
        // MCP and native input must share the same root-activity and principal policy.
        await new WorkspaceInject(brain, grains).InjectUserMessage(
            workspaceName, text, OwnerActor, cancellationToken, command).ConfigureAwait(false);
    }

    private static async Task<CallToolResult> WaitForResponseAsync(
        NeuronReference<IComposer> composer,
        CommandId commandId,
        long afterSequence,
        McpServer? server,
        CancellationToken cancellationToken)
    {
        await foreach (var page in composer.WatchJournalAsync(
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                if (ResultFromDelivery(delivery, commandId, server, includeUserAction: true) is { } result)
                {
                    return result;
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
    }

    private static CallToolResult? ResultFromDelivery(
        SignalDelivery delivery,
        CommandId commandId,
        McpServer? server,
        bool includeUserAction)
    {
        if (delivery.Signal is Responded responded && responded.CommandId == commandId)
        {
            if (responded.UserAction is not null && !includeUserAction)
            {
                return null;
            }

            return ResultFromUserAction(responded, server) ?? TextResult(responded.Text);
        }

        if (delivery.Signal is TurnLifecycle lifecycle
            && lifecycle.CommandId == commandId
            && lifecycle.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            var detail = string.IsNullOrWhiteSpace(lifecycle.Detail)
                ? $"Chat turn ended with status {lifecycle.Status}."
                : lifecycle.Detail;
            throw new InvalidOperationException(detail);
        }

        return null;
    }

    private static CallToolResult? ResultFromUserAction(Responded responded, McpServer? server)
    {
        if (responded.UserAction is not { } action)
        {
            return null;
        }

        if (server?.ClientCapabilities?.Elicitation?.Url is not null)
        {
            throw new UrlElicitationRequiredException(action.Message,
            [
                new ElicitRequestParams
                {
                    Mode = "url",
                    ElicitationId = action.Id,
                    Url = action.LoginUrl,
                    Message = action.Message,
                },
            ]);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = $"{action.Message}\n\n[Log in to {action.DisplayName}]({action.LoginUrl})\n\n"
                    + "After authorizing, repeat send_chat_message with the same text, commandId and chatName. Do not paste credentials into chat.",
            }],
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                status = nameof(ChatTurnStatus.WaitingForUser),
                commandId = responded.CommandId.ToString(),
                userAction = action,
            }, JsonSerializerOptions.Web),
        };
    }

    private static CallToolResult TextResult(string text)
        => new() { Content = [new TextContentBlock { Text = text }] };
}
