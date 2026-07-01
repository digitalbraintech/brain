using DigitalBrain.Google;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.google.gmail.v1")]
public class GmailNeuron(ILogger<GmailNeuron> logger, NeuronJournals journals, IGmailApiClient client)
    : Neuron(logger, journals), IGmailNeuron
{
    public Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default) =>
        client.ListMessagesAsync(query, maxResults, ct);

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default) =>
        client.ReadMessageAsync(messageId, ct);

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default) =>
        client.SendMessageAsync(to, subject, body, ct);
}
