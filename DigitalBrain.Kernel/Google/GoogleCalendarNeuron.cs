using DigitalBrain.Google;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.google.calendar.v1")]
public class GoogleCalendarNeuron(ILogger<GoogleCalendarNeuron> logger, NeuronJournals journals, IGoogleCalendarApiClient client)
    : Neuron(logger, journals), IGoogleCalendarNeuron
{
    public Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct = default) =>
        client.ListEventsAsync(timeMinIso, timeMaxIso, ct);

    public Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct = default) =>
        client.CreateEventAsync(summary, startIso, endIso, description, ct);

    public Task DeleteEventAsync(string eventId, CancellationToken ct = default) =>
        client.DeleteEventAsync(eventId, ct);
}
