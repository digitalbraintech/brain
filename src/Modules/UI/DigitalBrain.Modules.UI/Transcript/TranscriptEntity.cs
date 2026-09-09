using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(ITranscript.GrainTypeName)]
internal sealed class TranscriptEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TranscriptState> state)
    : Entity<TranscriptState>(state), ITranscript
{
    public async Task Append(TranscriptEntry entry, int cap)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        var current = State?.Entries ?? [];
        var next = current.Append(entry).ToArray();
        if (next.Length > cap)
        {
            next = next[(next.Length - cap)..];
        }

        await SaveAsync(new TranscriptState(next));
    }
}
