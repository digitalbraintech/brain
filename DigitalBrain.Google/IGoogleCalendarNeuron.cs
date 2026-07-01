using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public interface IGoogleCalendarNeuron : IAgent
{
    static string IAgent.AgentDisplayName => "Google Calendar";

    static string IAgent.AgentDescription =>
        "List, create, and delete events on the authenticated Google Calendar account.";

    static string[] IAgent.AgentCapabilities =>
        ["calendar", "google", "event", "schedule"];

    static string IAgent.AgentInstructions => """
        You are Google Calendar, the scheduling specialist. List, create, and delete events on the
        primary calendar. Create and delete mutate the user's calendar — confirm intent before those calls.
        """;

    [Description("List events between two ISO 8601 timestamps on the primary calendar.")]
    Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct = default);

    [Description("Create an event on the primary calendar. Mutates the calendar.")]
    Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct = default);

    [Description("Delete an event by its id. Mutates the calendar.")]
    Task DeleteEventAsync(string eventId, CancellationToken ct = default);
}
