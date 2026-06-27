// Travel domain pack. References ONLY DigitalBrain.Core + BCL so it embodies cleanly in a
// collectible ALC. Compiled into the test assembly for fast unit tests AND embedded as the
// pack Code string for the E2E publish (see TravelPackSource).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Tests.E2E.Packs;

public sealed record TravelFlight(string Id, string Airline, string From, string To, int Price, string Duration);
public sealed record TravelHotel(string Id, string Name, string City, int Price, double Rating);
public sealed record TravelEvent(string Id, string Title, string DateLabel, string Venue);
public sealed record TravelActivity(string Id, string Name, string Category, double Rating, string WeatherBadge);
public sealed record TravelWeather(string Destination, string Month, string Season, int AvgTempC, int RainPct);

internal static class TravelCorpus
{
    public static TravelWeather Weather(string destination, string month) =>
        new(destination, month, "dry", 28, 15);

    public static IReadOnlyList<TravelFlight> Flights() =>
    [
        new("FL-001", "Singapore Airlines", "London", "Bali", 850, "15h 45m"),
        new("FL-002", "Qatar Airways", "London", "Bali", 720, "18h 00m"),
    ];

    public static IReadOnlyList<TravelHotel> Hotels() =>
    [
        new("H-001", "Bambu Indah", "Ubud", 280, 4.7),
        new("H-002", "Como Uma Ubud", "Ubud", 420, 4.8),
    ];

    public static IReadOnlyList<TravelEvent> Events() =>
    [
        new("EV-001", "Gamelan Night at Ubud Palace", "Sat, Jun 14", "Ubud Palace"),
        new("EV-002", "Rice Terrace Sunrise Walk", "Sun, Jun 15", "Tegalalang"),
    ];

    public static IReadOnlyList<TravelActivity> Activities() =>
    [
        new("AC-001", "Tegalalang Rice Terraces", "Nature", 4.6, "Sunny day pick"),
        new("AC-002", "Agung Rai Museum of Art", "Museum", 4.6, "Cool off indoors"),
    ];

    public static TravelFlight? Flight(string? id) => Flights().FirstOrDefault(f => f.Id == id);
    public static TravelHotel? Hotel(string? id) => Hotels().FirstOrDefault(h => h.Id == id);
    public static TravelEvent? Event(string? id) => Events().FirstOrDefault(e => e.Id == id);
    public static TravelActivity? Activity(string? id) => Activities().FirstOrDefault(a => a.Id == id);

    public static string Destination(string prompt)
    {
        var marker = " to ";
        var i = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "your destination";
        var rest = prompt[(i + marker.Length)..].Trim();
        var word = rest.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "your destination";
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }
}

// Builds inline RFW DSL using brain's registered `digitalbrain` widgets. The DSL ships inside
// dataJson["source"]; brain's panel_manager reads it (panel_manager.dart:97).
internal static class TravelCards
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static string StepEvent(string ev, string argKey, string argValueExpr) =>
        $"event \"step\" {{ synapseType: \"ExperienceStep\", pack: \"travel\", experienceId: \"plan-trip\", eventName: \"{ev}\", {argKey}: {argValueExpr} }}";

    public static UiSurface Intro(TravelWeather w, IReadOnlyList<TravelFlight> flights)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = VStack(gap: 12.0, children: [\n");
        b.Append("  Panel(radius: 20.0, padding: 18.0, child: VStack(gap: 6.0, cross: \"start\", children: [\n");
        b.Append("    SectionLabel(text: \"WEATHER\"),\n");
        b.Append("    Text(text: data.weather.headline, variant: \"heading\"),\n");
        b.Append("  ])),\n");
        b.Append("  ...for f in data.flights:\n");
        b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
        b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: f.airline, variant: \"title\"), Text(text: f.duration, variant: \"dim\") ]),\n");
        b.Append($"      Button(label: \"Select\", onTap: {StepEvent("flight.selected", "flightId", "f.flightId")}),\n");
        b.Append("    ])),\n");
        b.Append("]);\n");

        var data = new
        {
            source = b.ToString(),
            weather = new { headline = $"{w.Month} in {w.Destination}: {w.Season} season, ~{w.AvgTempC}°C, {w.RainPct}% rain." },
            flights = flights.Select(f => new { airline = f.Airline, duration = f.Duration, flightId = f.Id }),
        };
        return Surface("travel-intro", data);
    }

    public static UiSurface Hotels(IReadOnlyList<TravelHotel> hotels)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = VStack(gap: 12.0, children: [\n");
        b.Append("  Panel(radius: 20.0, padding: 18.0, child: SectionLabel(text: \"HOTELS\")),\n");
        b.Append("  ...for h in data.hotels:\n");
        b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
        b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: h.name, variant: \"title\"), Stars(value: h.rating) ]),\n");
        b.Append($"      Button(label: \"Select\", onTap: {StepEvent("hotel.selected", "hotelId", "h.hotelId")}),\n");
        b.Append("    ])),\n");
        b.Append("]);\n");

        var data = new { source = b.ToString(), hotels = hotels.Select(h => new { name = h.Name, rating = h.Rating, hotelId = h.Id }) };
        return Surface("travel-hotels", data);
    }

    public static UiSurface Events(IReadOnlyList<TravelEvent> events)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = VStack(gap: 12.0, children: [\n");
        b.Append("  Panel(radius: 20.0, padding: 18.0, child: SectionLabel(text: \"EVENTS\")),\n");
        b.Append("  ...for e in data.events:\n");
        b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
        b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: e.title, variant: \"title\"), Text(text: e.dateLabel, variant: \"dim\") ]),\n");
        b.Append($"      Button(label: \"Select\", onTap: {StepEvent("event.selected", "eventId", "e.eventId")}),\n");
        b.Append("    ])),\n");
        b.Append($"  Button(label: \"Skip events\", onTap: event \"step\" {{ synapseType: \"ExperienceStep\", pack: \"travel\", experienceId: \"plan-trip\", eventName: \"events.skipped\" }}),\n");
        b.Append("]);\n");
        var data = new { source = b.ToString(), events = events.Select(e => new { title = e.Title, dateLabel = e.DateLabel, eventId = e.Id }) };
        return Surface("travel-events", data);
    }

    public static UiSurface Activities(IReadOnlyList<TravelActivity> activities)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = VStack(gap: 12.0, children: [\n");
        b.Append("  Panel(radius: 20.0, padding: 18.0, child: SectionLabel(text: \"ACTIVITIES\")),\n");
        b.Append("  ...for a in data.activities:\n");
        b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
        b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: a.name, variant: \"title\"), Badge(text: a.weatherBadge, tone: \"teal\") ]),\n");
        b.Append($"      Button(label: \"Select\", onTap: {StepEvent("activity.selected", "activityId", "a.activityId")}),\n");
        b.Append("    ])),\n");
        b.Append("]);\n");
        var data = new { source = b.ToString(), activities = activities.Select(a => new { name = a.Name, weatherBadge = a.WeatherBadge, activityId = a.Id }) };
        return Surface("travel-activities", data);
    }

    public static UiSurface Summary(string destination, string flightAirline, string hotelName, string eventTitle, string activityName)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = Panel(radius: 20.0, padding: 18.0, child: VStack(gap: 8.0, cross: \"start\", children: [\n");
        b.Append("  SectionLabel(text: \"TRIP SUMMARY\"),\n");
        b.Append("  Text(text: data.destination, variant: \"heading\"),\n");
        b.Append("  KeyValue(label: \"Flight\", value: data.flight),\n");
        b.Append("  KeyValue(label: \"Hotel\", value: data.hotel),\n");
        b.Append("  KeyValue(label: \"Event\", value: data.eventTitle),\n");
        b.Append("  KeyValue(label: \"Activity\", value: data.activity),\n");
        b.Append("]));\n");
        var data = new
        {
            source = b.ToString(),
            destination,
            flight = flightAirline,
            hotel = hotelName,
            eventTitle,
            activity = activityName,
        };
        return Surface("travel-summary", data);
    }

    public static UiSurface Surface(string surfaceId, object data)
    {
        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = "digitalbrain",
            ["rootWidget"] = "root",
            ["dataJson"] = JsonSerializer.Serialize(data, Json),
            [UiSurfaceKeys.SurfaceId] = surfaceId,
            [UiSurfaceKeys.Emitter] = "travel",
            [UiSurfaceKeys.Title] = "Plan a trip",
        };
        return new UiSurface(UiSurface.RfwKind, props);
    }
}

public sealed class TravelPackBehavior : IPackBehavior
{
    string _destination = "your destination";
    string _month = "this season";
    TravelFlight? _flight;
    TravelHotel? _hotel;
    TravelEvent? _event;

    public string Respond(string input) => $"travel: {input}";

    public PackManifest GetManifest() => new([new SynapseType(nameof(ExperienceStep))]);

    public bool CanHandle(Synapse synapse) => synapse is ExperienceStep;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not ExperienceStep step) return [];

        switch (step.EventName)
        {
            case "start":
                var prompt = step.Args.GetValueOrDefault("prompt", "plan a trip to Bali next month");
                _destination = TravelCorpus.Destination(prompt);
                _month = "next month";
                return [TravelCards.Intro(TravelCorpus.Weather(_destination, _month), TravelCorpus.Flights())];

            case "flight.selected":
                _flight = TravelCorpus.Flight(step.Args.GetValueOrDefault("flightId"));
                return [TravelCards.Hotels(TravelCorpus.Hotels())];

            case "hotel.selected":
                _hotel = TravelCorpus.Hotel(step.Args.GetValueOrDefault("hotelId"));
                return [TravelCards.Events(TravelCorpus.Events())];

            case "event.selected":
                _event = TravelCorpus.Event(step.Args.GetValueOrDefault("eventId"));
                return [TravelCards.Activities(TravelCorpus.Activities())];

            case "events.skipped":
                _event = null;
                return [TravelCards.Activities(TravelCorpus.Activities())];

            case "activity.selected":
                var activity = TravelCorpus.Activity(step.Args.GetValueOrDefault("activityId"));
                return
                [
                    TravelCards.Summary(
                        _destination,
                        _flight?.Airline ?? "(none)",
                        _hotel?.Name ?? "(none)",
                        _event?.Title ?? "(skipped)",
                        activity?.Name ?? "(none)")
                ];

            default:
                return [];
        }
    }
}
