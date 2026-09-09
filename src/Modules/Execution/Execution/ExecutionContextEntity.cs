using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Execution;

[GrainType("executioncontext")]
internal sealed class ExecutionContextEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ExecutionContextState> state)
    : Entity<ExecutionContextState>(state), IExecutionContext
{
    public Task<ContextEntry?> Query(ContextQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (State is null)
        {
            return Task.FromResult<ContextEntry?>(null);
        }

        for (var i = 0; i < State.Slots.Count; i++)
        {
            var slot = State.Slots[i];
            if (string.Equals(slot.Path.Value, query.Path.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<ContextEntry?>(slot.Entry);
            }
        }

        return Task.FromResult<ContextEntry?>(null);
    }

    public async Task Ensure(ExecutionId executionId)
    {
        RequireMatchingKey(executionId);

        if (State is not null)
        {
            return;
        }

        await SaveAsync(new ExecutionContextState(executionId, []));
    }

    public async Task ApplyDelta(ContextDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        var executionId = State?.ExecutionId ?? ParseExecutionIdFromKey();
        RequireMatchingKey(executionId);

        var slots = new List<ContextSlot>(State?.Slots.Count ?? 0);
        if (State?.Slots is { } existing)
        {
            for (var i = 0; i < existing.Count; i++)
            {
                if (!string.Equals(existing[i].Path.Value, delta.Path.Value, StringComparison.Ordinal))
                {
                    slots.Add(existing[i]);
                }
            }
        }

        slots.Add(new ContextSlot(
            delta.Path,
            new ContextEntry(
                delta.SchemaHash,
                delta.PayloadJson,
                delta.BlobRef,
                DigestOf(delta))));

        await SaveAsync(new ExecutionContextState(executionId, slots));
    }

    private void RequireMatchingKey(ExecutionId executionId)
    {
        var keyId = ParseExecutionIdFromKey();
        if (keyId != executionId)
        {
            throw new InvalidOperationException(
                $"Execution context grain '{this.GetPrimaryKeyString()}' cannot bind execution '{executionId}'.");
        }
    }

    private ExecutionId ParseExecutionIdFromKey()
    {
        var grainKey = this.GetPrimaryKeyString();
        var separator = grainKey.IndexOf('/');
        if (separator <= 0 || separator == grainKey.Length - 1)
        {
            throw new InvalidOperationException(
                $"Execution context grain key '{grainKey}' is not in owner/name form.");
        }

        return ExecutionId.Parse(grainKey[(separator + 1)..]);
    }

    private static ContextDigest DigestOf(ContextDelta delta)
    {
        var material = string.Concat(
            delta.SchemaHash,
            "\0",
            delta.PayloadJson ?? string.Empty,
            "\0",
            delta.BlobRef ?? string.Empty);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
        return new ContextDigest(hash);
    }
}
