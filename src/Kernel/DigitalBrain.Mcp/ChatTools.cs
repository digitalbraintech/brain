using DigitalBrain.Product.Identity;
using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
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
    [Description("Inject UserMessaged into the owner inbox for a workspace. Ino answers along the start.cs synapse. If a login action is required, complete it in the browser and retry with the same text, commandId and chatName. Never send credentials in chat.")]
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
        var ino = brain.Get<IAssistant>("assistant");
        var before = await ino.ReadJournalAsync(JournalKind.Outgoing, long.MaxValue, cancellationToken)
            .ConfigureAwait(false);
        await InjectAsync(chatName, text, command, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(ino, command, before.ResumeSequence, server, timeout.Token)
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
        var index = brain.GetEntity<IWorkspaceIndex>(IWorkspaceIndex.DefaultInstanceName);
        var record = await index.Find(workspaceName).ConfigureAwait(false);
        if (record is null)
        {
            if (!string.Equals(workspaceName, "main", StringComparison.Ordinal))
            {
                throw new NeuronAuthorizationException($"Unknown workspace '{workspaceName}'.");
            }

            await index.Ensure("main", "Main").ConfigureAwait(false);
            record = await index.Find("main").ConfigureAwait(false)
                ?? throw new InvalidOperationException("Workspace 'main' could not be created.");
        }

        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var correlation = new CorrelationId(Guid.Parse(record.CorrelationId));
        var session = grains.GetGrain<IBrainNeuron>(IBrainNeuron.ForOwner(brain.Owner).ToGrainId());
        await session.SendWithCorrelation(
                inbox.Id,
                new UserMessaged(command, inbox.Id, text, OwnerActor),
                correlation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<CallToolResult> WaitForResponseAsync(
        NeuronReference<IAssistant> ino,
        CommandId commandId,
        long afterSequence,
        McpServer? server,
        CancellationToken cancellationToken)
    {
        await foreach (var page in ino.WatchJournalAsync(
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Signal is Responded responded && responded.CommandId == commandId)
                {
                    if (ResultFromUserAction(responded, server) is { } pending)
                    {
                        return pending;
                    }

                    return TextResult(responded.Text);
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
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
