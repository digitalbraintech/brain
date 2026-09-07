using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Chat;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

public sealed class WorkspaceChatScenarioDriver(IGrainFactory grains) : IApplicationScenarioDriver
{
    public string Kind => "chat.user-message/v1";

    public async Task<string> RunAsync(IDigitalBrain brain, string applicationKey, JsonElement stimulus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationKey);
        RequireShape(stimulus);
        var conversation = stimulus.GetProperty("conversation").GetString();
        var text = stimulus.GetProperty("text").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var actor = VerifiedActor.Current
            ?? throw new InvalidOperationException("Chat scenarios require an authenticated scenario actor.");
        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var command = await new WorkspaceInject(brain, grains).InjectUserMessage(
            conversation, text, actor, cancellationToken).ConfigureAwait(false);

        var cursor = 0L;
        while (true)
        {
            var read = await inbox.ReadJournalAsync(JournalKind.Outgoing, cursor, cancellationToken)
                .ConfigureAwait(false);
            var response = read.Delta.FirstOrDefault(delivery =>
                delivery.Principal == actor.PrincipalId
                && delivery.Signal is Responded replied
                && replied.CommandId == command
                && replied.Chat == inbox.Id);
            if (response?.Signal is Responded replied)
            {
                return JsonSerializer.Serialize(new
                {
                    kind = "chat.responded/v1",
                    conversation,
                    text = replied.Text,
                });
            }
            cursor = read.ResumeSequence;
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void RequireShape(JsonElement stimulus)
    {
        if (stimulus.ValueKind != JsonValueKind.Object
            || stimulus.EnumerateObject().Any(property => property.Name is not ("kind" or "conversation" or "text"))
            || stimulus.GetProperty("kind").GetString() != "chat.user-message/v1")
        {
            throw new InvalidOperationException("A chat scenario requires only kind, conversation, and text.");
        }
    }
}
