using System.Threading.Channels;
using DigitalBrain.Abstractions.Journals;

namespace DigitalBrain.Abstractions;

internal sealed class ChannelJournalObserver : IJournalObserver
{
    private readonly Channel<JournalRead> _reads = Channel.CreateUnbounded<JournalRead>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly JournalKind _kind;

    public ChannelJournalObserver(JournalKind kind)
    {
        _kind = kind;
    }

    public ChannelReader<JournalRead> Reads => _reads.Reader;

    public Task ObserveAsync(JournalKind kind, JournalRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (kind != _kind)
        {
            return Task.FromException(new InvalidOperationException(
                $"Journal observer for '{_kind}' received a '{kind}' batch."));
        }

        return _reads.Writer.WriteAsync(read).AsTask();
    }

    public void Complete(Exception? failure = null)
        => _reads.Writer.TryComplete(failure);
}
