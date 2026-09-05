using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Memory;

[GrainType("memory")]
internal sealed class MemoryEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<MemoryState> state)
    : Entity<MemoryState>(state), IMemory
{
    private const int MaxFacts = 4096;
    private const int MaxText = 8192;

    public async Task Upsert(MemoryFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact.Key);
        if (fact.Text.Length > MaxText)
        {
            throw new ArgumentException("A memory fact cannot exceed 8192 characters.", nameof(fact));
        }

        var current = State?.Facts ?? [];
        var without = current.Where(existing => !string.Equals(existing.Key, fact.Key, StringComparison.Ordinal));
        var next = without.Append(fact).ToArray();
        if (next.Length > MaxFacts)
        {
            next = next[(next.Length - MaxFacts)..];
        }

        await SaveAsync(new MemoryState(next));
    }

    public async Task Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var current = State?.Facts ?? [];
        await SaveAsync(new MemoryState([.. current.Where(fact => !string.Equals(fact.Key, key, StringComparison.Ordinal))]));
    }
}
