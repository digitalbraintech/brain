using DigitalBrain.Windows;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.sdk.filesystem.v1")]
public class FileSystemNeuron(ILogger<FileSystemNeuron> logger, NeuronJournals journals, FileSystemOperations ops)
    : Neuron(logger, journals), IFileSystemNeuron
{
    public Task<string> ReadFileAsync(string path, CancellationToken ct = default) => ops.ReadFileAsync(path, ct);
    public Task WriteFileAsync(string path, string content, CancellationToken ct = default) => ops.WriteFileAsync(path, content, ct);
    public Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default) => ops.ListFilesAsync(directory, pattern, ct);
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => ops.ExistsAsync(path, ct);
    public Task<string> CopyAsync(string source, string destination, CancellationToken ct = default) => ops.CopyAsync(source, destination, ct);
    public Task<string> MoveAsync(string source, string destination, CancellationToken ct = default) => ops.MoveAsync(source, destination, ct);
    public Task<string> DeleteAsync(string path, CancellationToken ct = default) => ops.DeleteAsync(path, ct);
    public Task<string> GetInfoAsync(string path, CancellationToken ct = default) => ops.GetInfoAsync(path, ct);
}

