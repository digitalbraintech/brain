using System.Security.Cryptography;
using System.Text;
using Orleans.Journaling;

namespace DigitalBrain.AI;

public abstract partial class Agent
{
    private readonly IDurableDictionary<string, AgentRequestResult> _completedRequests;
    private async Task<AgentReply> AskDurablyAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (CurrentDelivery is not { SourceEpoch: not null } delivery)
        {
            return await Ask(request, cancellationToken).ConfigureAwait(true);
        }
        var completed = _completedRequests;
        var key = $"{delivery.Caller}:{delivery.SignalId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)));
        if (completed.TryGetValue(key, out var existing))
        {
            if (existing.RequestHash != hash)
            {
                throw new InvalidOperationException("A completed model request identity cannot be reused with different input.");
            }
            return existing.Reply;
        }
        if (completed.Count >= 10000)
        {
            throw new InvalidOperationException("This agent has reached its durable request capacity. Create a new named agent to continue.");
        }
        var reply = await Ask(request, cancellationToken).ConfigureAwait(true);
        completed[key] = new(hash, reply);
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
        return reply;
    }
}

[GenerateSerializer, Alias("db.agent-request-result")]
internal sealed record AgentRequestResult(
    [property: Id(0)] string RequestHash,
    [property: Id(1)] AgentReply Reply);
