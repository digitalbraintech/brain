using DigitalBrain.Google;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.google.drive.v1")]
public class GoogleDriveNeuron(ILogger<GoogleDriveNeuron> logger, NeuronJournals journals, IGoogleDriveApiClient client)
    : Neuron(logger, journals), IGoogleDriveNeuron
{
    public Task<string[]> ListFilesAsync(string query, CancellationToken ct = default) =>
        client.ListFilesAsync(query, ct);

    public Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct = default) =>
        client.UploadFileAsync(name, content, mimeType, ct);

    public Task<string> DownloadFileAsync(string fileId, CancellationToken ct = default) =>
        client.DownloadFileAsync(fileId, ct);

    public Task DeleteFileAsync(string fileId, CancellationToken ct = default) =>
        client.DeleteFileAsync(fileId, ct);
}
