using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

[GrainType("digitalbrain.sdk.filesystem.v1")]
public class FileSystemNeuron : Neuron, IFileSystemNeuron
{
    private const int MaxReadChars = 50 * 1024;

    public FileSystemNeuron(ILogger<FileSystemNeuron> logger) : base(logger) { }

    public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        return text.Length > MaxReadChars ? text[..MaxReadChars] + "\n... [truncated]" : text;
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, content, ct);
    }

    public Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default)
        => Task.FromResult(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly));

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(File.Exists(path) || Directory.Exists(path));

    public Task<string> CopyAsync(string source, string destination, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.Copy(source, destination, overwrite: true);
        return Task.FromResult($"Copied '{source}' -> '{destination}'.");
    }

    public Task<string> MoveAsync(string source, string destination, CancellationToken ct = default)
    {
        File.Move(source, destination, overwrite: true);
        return Task.FromResult($"Moved '{source}' -> '{destination}'.");
    }

    public Task<string> DeleteAsync(string path, CancellationToken ct = default)
    {
        // For safety, never delete directories (matches IAW's FileSystem agent).
        if (Directory.Exists(path))
            return Task.FromResult($"Refused: '{path}' is a directory.");
        File.Delete(path);
        return Task.FromResult($"Deleted '{path}'.");
    }

    public Task<string> GetInfoAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return Task.FromResult($"File '{path}': {info.Length} bytes, modified {info.LastWriteTimeUtc:u}.");
        }
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return Task.FromResult($"Directory '{path}': created {info.CreationTimeUtc:u}.");
        }
        return Task.FromResult($"Path '{path}' does not exist.");
    }
}
