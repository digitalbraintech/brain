using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.UI;

[GrainType("usermessages")]
internal sealed class UserMessagesNeuron : Neuron, IComposer
{
    private const int ResponseCapacity = 100_000;
    private readonly IApplicationChatIngress applications;
    private readonly IDurableDictionary<string, string> publishedResponses;

    public UserMessagesNeuron(NeuronRuntime runtime, IApplicationChatIngress applications) : base(runtime)
    {
        this.applications = applications;
        publishedResponses = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, string>>(
            "ui.composer-responses.v1");
    }

    public async Task HandleAsync(UserMessaged signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var cause = CurrentDelivery ?? throw new InvalidOperationException("UserMessages requires a delivery.");
        var principal = cause.Principal ?? throw new NeuronAuthorizationException(
            "Application chat input requires an authenticated principal.");
        if (signal.Actor?.PrincipalId != principal || cause.Caller != IBrainNeuron.ForOwner(Id.Owner))
        {
            throw new NeuronAuthorizationException("UserMessages must retain its verified owner and principal.");
        }

        var admission = await applications.AdmitAsync(Id.Owner, principal, signal.CommandId.Value,
            cause.CorrelationId.Value, Id.ToString(), "chat.user-messaged/v1", signal.Text,
            JsonSerializer.Serialize(signal), cancellationToken).ConfigureAwait(true);
        if (admission is null)
        {
            var recipients = await BroadcastAsync(signal, cause).ConfigureAwait(true);
            if (recipients == 0 && !await applications.HasFallbackAsync(
                Id.Owner, principal, Id.ToString(), cancellationToken).ConfigureAwait(true))
            {
                await RecordOutgoingAsync(new TurnLifecycle(
                    new TurnId(signal.CommandId.Value), signal.CommandId, Id,
                    ChatTurnStatus.Failed,
                    "No application or signal recipient accepted the user message."),
                    cause.CorrelationId).ConfigureAwait(true);
            }
            return;
        }

        try
        {
            var result = await applications.AwaitAsync(admission, cancellationToken).ConfigureAwait(true);
            await PublishResponseAsync(
                new Responded(signal.CommandId, Id, result.Output, result.ApplicationKey),
                cause.CorrelationId).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await RecordOutgoingAsync(new TurnLifecycle(
                new TurnId(signal.CommandId.Value), signal.CommandId, Id,
                ChatTurnStatus.Failed, error.Message), cause.CorrelationId).ConfigureAwait(true);
        }
        catch (OperationCanceledException error)
        {
            await RecordOutgoingAsync(new TurnLifecycle(
                new TurnId(signal.CommandId.Value), signal.CommandId, Id,
                ChatTurnStatus.Cancelled, error.Message), cause.CorrelationId).ConfigureAwait(true);
        }
    }

    public async Task HandleAsync(Responded signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var cause = CurrentDelivery ?? throw new InvalidOperationException("A composer response requires a delivery.");
        if (cause.Caller != new NeuronId("assistant", Id.Owner, "assistant") || cause.Principal is null
            || signal.Chat != Id)
        {
            throw new NeuronAuthorizationException("Only this owner's verified assistant may publish a fallback response.");
        }
        await PublishResponseAsync(signal, cause.CorrelationId).ConfigureAwait(true);
    }

    public async Task HandleAsync(TurnLifecycle signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var cause = CurrentDelivery ?? throw new InvalidOperationException("A composer lifecycle requires a delivery.");
        if (cause.Caller != new NeuronId("assistant", Id.Owner, "assistant") || cause.Principal is null
            || signal.Chat != Id)
        {
            throw new NeuronAuthorizationException(
                "Only this owner's verified assistant may publish a terminal lifecycle.");
        }
        if (signal.Status is not (ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
            || signal.TurnId.Value != signal.CommandId.Value)
        {
            throw new InvalidOperationException(
                "The assistant may publish only a matching failed or cancelled turn lifecycle.");
        }

        await RecordOutgoingAsync(signal, cause.CorrelationId).ConfigureAwait(true);
    }

    private async Task PublishResponseAsync(Responded response, CorrelationId correlation)
    {
        var principal = CurrentDelivery?.Principal ?? throw new NeuronAuthorizationException(
            "A composer response requires an authenticated principal.");
        var key = $"{principal}/{response.CommandId.Value:N}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[]
            {
                correlation.Value.ToString("N"), response.Chat.ToString(), response.Text, response.Author,
            }))));
        if (publishedResponses.TryGetValue(key, out var existing))
        {
            if (!string.Equals(existing, hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A composer response identity cannot be reused with different content.");
            }
            return;
        }
        EnsureResponseCapacity(publishedResponses.Count);

        // The journal append and its tombstone are persisted by RecordOutgoingAsync's one grain-state write.
        publishedResponses[key] = hash;
        await RecordOutgoingAsync(response, correlation).ConfigureAwait(true);
    }

    internal static void EnsureResponseCapacity(int publishedCount)
    {
        if (publishedCount >= ResponseCapacity)
        {
            throw new InvalidOperationException("The composer response retention capacity is exhausted.");
        }
    }
}
