using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Orleans.Journaling;

namespace DigitalBrain.Testing;

// Test persistence boundary: the production durable-grain journal codec writes its
// actual bytes here. A replacement silo reads a new provider backed by these files.
internal sealed class FileJournalStorageProvider(string root) : IJournalStorageProvider
{
    private readonly ConcurrentDictionary<string, FileJournalStorage> _journals = new(StringComparer.Ordinal);

    public IJournalStorage CreateStorage(JournalId journalId)
        => _journals.GetOrAdd(journalId.Value, id => new(Path.Combine(root, "journals",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".journal")));
}

internal sealed class FileJournalStorage(string path) : IJournalStorage
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsCompactionRequested => false;

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : [];
            consumer.Read(new ReadOnlyMemory<byte>(bytes), metadata: null, complete: true);
        }
        finally { _gate.Release(); }
    }

    public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        => WriteAsync(value, append: true, cancellationToken);

    public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        => WriteAsync(value, append: false, cancellationToken);

    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { File.Delete(path); }
        finally { _gate.Release(); }
    }

    private async ValueTask WriteAsync(ReadOnlySequence<byte> value, bool append, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (append && File.Exists(path))
                {
                    await using var previous = File.OpenRead(path);
                    await previous.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
                foreach (var segment in value)
                {
                    await output.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
                }
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
            _gate.Release();
        }
    }
}
