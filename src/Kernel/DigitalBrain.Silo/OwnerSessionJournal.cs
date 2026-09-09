using DigitalBrain.Chat;
using DigitalBrain.Abstractions;
using DigitalBrain.UI;

using DigitalBrain.Abstractions.Journals;
namespace DigitalBrain.Kernel;

internal sealed class OwnerSessionJournal(IDigitalBrain brain)
{
    public IAsyncEnumerable<JournalRead> WatchSurfaceOutgoingAsync(
        string surfaceName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        // SurfaceOpened is recorded by the renderer instance sharing the surface's name.
        return brain.Get<IUIRenderer>(surfaceName)
            .WatchJournalAsync(JournalKind.Outgoing, afterSequence, cancellationToken);
    }

    public IAsyncEnumerable<JournalRead> WatchChatOutgoingAsync(
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.Get<IChat>(chatName)
            .WatchJournalAsync(JournalKind.Outgoing, afterSequence, cancellationToken);
    }

}
