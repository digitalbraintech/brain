using System.ComponentModel;

namespace DigitalBrain.Protocol;

// Typed filesystem operations. Re-homed from IAW's IFileSystem/FileSystemAgent onto Neuron (core ops subset).
public interface IFileSystemNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "FileSystem";

    static string INeuronAgent.AgentDescription =>
        "Read, write, list, copy, move, delete, and inspect files anywhere on the host.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["file", "read", "write", "copy", "move", "delete", "list", "filesystem"];

    static string INeuronAgent.AgentInstructions => """
        You are FileSystem, the file operations specialist.

        RULES:
        - Execute file operations immediately — never give manual instructions.
        - Absolute paths work as-is; writing auto-creates parent directories.
        - Delete refuses directories for safety. Do NOT analyze code (use Roslyn) or build (use DotNet).
        """;

    [Description("Read a file's contents (truncated to 50KB for large files).")]
    Task<string> ReadFileAsync(string path, CancellationToken ct = default);

    [Description("Write content to a file, creating it and parent directories if needed.")]
    Task WriteFileAsync(string path, string content, CancellationToken ct = default);

    [Description("List files in a directory matching a glob pattern. Default '*' lists all.")]
    Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default);

    [Description("Return whether a file or directory exists at the path.")]
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    [Description("Copy a file to a new location (overwrites; creates parent directories).")]
    Task<string> CopyAsync(string source, string destination, CancellationToken ct = default);

    [Description("Move or rename a file (overwrites destination).")]
    Task<string> MoveAsync(string source, string destination, CancellationToken ct = default);

    [Description("Delete a file. For safety, refuses directories.")]
    Task<string> DeleteAsync(string path, CancellationToken ct = default);

    [Description("Get file or directory metadata: size and timestamps.")]
    Task<string> GetInfoAsync(string path, CancellationToken ct = default);
}
