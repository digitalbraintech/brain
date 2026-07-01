using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public interface IGoogleDriveNeuron : IAgent
{
    static string IAgent.AgentDisplayName => "Google Drive";

    static string IAgent.AgentDescription =>
        "List, upload, download, and delete files in the authenticated Google Drive account.";

    static string[] IAgent.AgentCapabilities =>
        ["drive", "google", "file", "upload", "download", "delete"];

    static string IAgent.AgentInstructions => """
        You are Google Drive, the cloud file specialist. List, upload, download, and delete files.
        Delete and upload mutate the user's Drive — confirm intent before those calls.
        """;

    [Description("List files matching a Drive search query.")]
    Task<string[]> ListFilesAsync(string query, CancellationToken ct = default);

    [Description("Upload a file with the given name, text content, and MIME type. Mutates Drive.")]
    Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct = default);

    [Description("Download a file's text content by its Drive file id.")]
    Task<string> DownloadFileAsync(string fileId, CancellationToken ct = default);

    [Description("Delete a file by its Drive file id. Mutates Drive.")]
    Task DeleteFileAsync(string fileId, CancellationToken ct = default);
}
