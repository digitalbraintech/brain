using DigitalBrain.Core;
using DigitalBrain.UiKit;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.data-visualization.v1")]
public class ChartNeuron : Neuron, IChartNeuron, IDataVisualizationNeuron
{
    private readonly ConcurrentDictionary<string, ChartSession> _sessions = new(StringComparer.Ordinal);

    public ChartNeuron(ILogger<ChartNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public static string AgentDisplayName => "Chart";
    public static string AgentDescription => "First-class interactive grammar-of-graphics charts with analysis and conversational modification.";
    public static string[] AgentCapabilities => new[] { "chart", "visualize", "analyze", "filter", "transform", "highlight" };
    public static string AgentInstructions => "You are Chart. Visualize data and modify live views on natural language instructions. Maintain per-surface context and emit updated surfaces using the same surfaceId for live panel identity.";
    public static string[] AgentRoutingExamples => new[] { "show sales trend", "filter last 7 days", "switch to area chart and highlight outliers" };

    public async Task HandleAsync(VisualizeDataRequest request)
    {
        var surfaceId = !string.IsNullOrWhiteSpace(request.RequestId) ? request.RequestId! : request.CorrelationId ?? "chart-" + Guid.NewGuid().ToString("N")[..8];
        var rows = DataChartBuilder.ParseRows(request.DataJson).Select(r => (IReadOnlyDictionary<string, object?>)r).ToArray();

        var session = _sessions.GetOrAdd(surfaceId, _ => new ChartSession { SurfaceId = surfaceId });
        session.Rows = rows;
        session.OriginalRows = rows;

        var spec = BuildGraphicSpec(session, request.Prompt, request.ChartHint);
        var surface = ScopeSurface(UiSurfaceSamples.Chart(surfaceId, Self.Value, spec), request.UserId, request.SessionId);

        var sender = request.Sender?.Value ?? CurrentCause?.Sender?.Value ?? "";
        if (sender.Contains("tg-chat", StringComparison.OrdinalIgnoreCase))
        {
            var merged = new Dictionary<string, object?>(surface.Props)
            {
                ["originChannel"] = "telegram",
                ["fromTelegram"] = true,
                ["channelContext"] = "excel-like from tg"
            };
            if (merged.TryGetValue("title", out var t) && t is string title)
            {
                merged["title"] = title + " (from Telegram)";
            }
            surface = surface with { Props = merged };
        }

        await FireAsync(new DataChartGenerated(surfaceId, surface));
        await BroadcastRfwCard(surface);

        // P0-5: also deliver UiSurface to dedicated FlutterUiNeuron (point-to-point via I* contract) so it owns handling for thin client.
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(surface.Stamp(Self, CurrentCause));
    }

    public async Task HandleAsync(ChartCommand command)
    {
        if (!_sessions.TryGetValue(command.SurfaceId, out var session))
        {
            await FireAsync(new DataChartFailed(command.SurfaceId, "no session"));
            return;
        }

        var chat = ServiceProvider.GetService<IChatClient>();
        var instruction = command.Instruction.ToLowerInvariant();

        // LLM assisted action inference (fall back to string match)
        var action = "passthrough";
        string? arg = null;
        if (chat != null)
        {
            var summary = $"{session.Rows.Count} rows";
            try
            {
                var resp = await chat.GetResponseAsync($"Data: {summary}. Instruction: {command.Instruction}. Reply with short action like: filter:7, area, outliers, group, remove-below:10");
                var t = (resp.Text ?? "").ToLowerInvariant();
                if (t.Contains("filter") || instruction.Contains("last") || instruction.Contains("7 day")) { action = "filter"; arg = "7"; }
                else if (t.Contains("area")) action = "area";
                else if (t.Contains("outlier")) action = "outliers";
                else if (t.Contains("group")) action = "group";
                else if (t.Contains("remove") || t.Contains("below")) { action = "remove-below"; arg = "0"; }
            }
            catch { }
        }

        if (instruction.Contains("7") || instruction.Contains("last")) { action = "filter"; arg = "7"; }
        if (instruction.Contains("area")) action = "area";
        if (instruction.Contains("outlier")) action = "outliers";

        session.Rows = Apply(session, action, arg);

        var newSpec = BuildGraphicSpec(session, instruction, null);
        session.Current = newSpec;

        var s = UiSurfaceSamples.Chart(command.SurfaceId, Self.Value, newSpec);
        await FireAsync(new DataChartGenerated(command.SurfaceId, s));
        await BroadcastRfwCard(s);
    }

    public async Task HandleAsync(ChartInteraction inter)
    {
        if (!_sessions.TryGetValue(inter.SurfaceId, out var session) || session.Current == null) return;
        var s = UiSurfaceSamples.Chart(inter.SurfaceId, Self.Value, session.Current);
        await FireAsync(new DataChartGenerated(inter.SurfaceId, s));
        await BroadcastRfwCard(s);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Apply(ChartSession ses, string action, string? arg)
    {
        var rows = ses.Rows;
        if (action == "filter")
        {
            int n = 7;
            if (int.TryParse(arg, out var v)) n = v;
            return rows.TakeLast(Math.Max(1, Math.Min(n, rows.Count))).ToList();
        }
        if (action == "remove-below")
        {
            double th = 0;
            double.TryParse(arg, out th);
            return rows.Where(r => GetFirstNumber(r) >= th).ToList();
        }
        return rows;
    }

    private static double GetFirstNumber(IReadOnlyDictionary<string, object?> r)
    {
        foreach (var v in r.Values)
        {
            if (v is double d) return d;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2)) return d2;
        }
        return 0;
    }

    private GraphicSpec BuildGraphicSpec(ChartSession ses, string prompt, string? hint)
    {
        var rows = ses.Rows;
        var type = DataChartBuilder.ChooseChartType(prompt, hint);
        var (x, y, ser) = DataChartBuilder.InferEncoding(rows, type);

        var vars = new Dictionary<string, object?>
        {
            [x] = new Dictionary<string, object?> { ["type"] = "ordinal" },
            [y] = new Dictionary<string, object?> { ["type"] = "linear" }
        };
        if (ser != null) vars[ser] = new Dictionary<string, object?> { ["type"] = "nominal" };

        var marks = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["kind"] = type == "area" ? "area" : type == "line" ? "line" : "interval", ["position"] = x + "*" + y }
        };

        var title = string.IsNullOrWhiteSpace(prompt) ? type + " chart" : (prompt.Length > 55 ? prompt[..55] + "..." : prompt);
        var sum = $"{rows.Count} rows. {type} view.";

        var g = new GraphicSpec(title, rows, vars, marks, null, sum);
        ses.Current = g;
        return g;
    }

    private static UiSurface ScopeSurface(UiSurface surface, string? userId, string? sessionId)
    {
        var props = new Dictionary<string, object?>(surface.Props)
        {
            ["userId"] = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim(),
            ["sessionId"] = sessionId
        };

        return surface with { Props = props };
    }

    private async Task BroadcastRfwCard(UiSurface surface)
    {
        // Prefer routing through dedicated IFlutterUiNeuron (item 14) so it owns the UI channel, applies bridge, and broadcasts.
        // Uses CorrelationId from surface for context sharing (per channel marker).
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    public new Task<IReadOnlyList<Synapse>> GetTimelineAsync() => Task.FromResult<IReadOnlyList<Synapse>>(OutgoingJournal.ToList());

    private sealed class ChartSession
    {
        public string SurfaceId = "";
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows = Array.Empty<IReadOnlyDictionary<string, object?>>();
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> OriginalRows = Array.Empty<IReadOnlyDictionary<string, object?>>();
        public GraphicSpec? Current;
    }
}

// Backward compat alias for old code / tests / MCP
public class DataVisualizationNeuron : ChartNeuron
{
    public DataVisualizationNeuron(ILogger<DataVisualizationNeuron> logger, NeuronJournals journals) : base(logger, journals) { }
}

// Minimal static helpers for reuse (no full original duplication)
public static class DataChartBuilder
{
    public static string ChooseChartType(string prompt, string? chartHint) => ChartNeuronDataChartBuilderShim.Choose(prompt, chartHint);
    public static (string X, string Y, string? Series) InferEncoding(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string chartType) => ChartNeuronDataChartBuilderShim.Infer(rows, chartType);
    public static IReadOnlyList<Dictionary<string, object?>> ParseRows(string dataJson) => ChartNeuronDataChartBuilderShim.Parse(dataJson);

    public static UiSurface BuildSurface(string requestId, string emitter, string prompt, string dataJson, string? chartHint = null)
    {
        var rows = ParseRows(dataJson).Select(r => (IReadOnlyDictionary<string, object?>)r).ToArray();
        var ct = ChooseChartType(prompt, chartHint);
        var (x, y, ser) = InferEncoding(rows, ct);
        var title = string.IsNullOrWhiteSpace(prompt) ? ct + " chart" : prompt;
        var spec = new ChartSpec(title, ct, rows, x, y, ser, null, true, ct != "pie", $"{rows.Length} rows.");
        return UiSurfaceSamples.DataChart("surface.data-chart." + requestId, emitter, spec);
    }

    // Shim to avoid name conflict during edits
    internal static class ChartNeuronDataChartBuilderShim
    {
        private static readonly string[] RowArrayNames = ["rows", "data", "items", "values", "records"];
        private static readonly string[] Valid = ["bar", "line", "area", "scatter", "pie"];

        public static string Choose(string p, string? h)
        {
            var hh = (h ?? "").Trim().ToLowerInvariant();
            var direct = Valid.FirstOrDefault(t => hh == t || hh.Contains(t));
            if (direct != null) return direct;
            var txt = (hh + " " + (p ?? "")).ToLowerInvariant();
            if (txt.Contains("pie") || txt.Contains("share") || txt.Contains("percent")) return "pie";
            if (txt.Contains("scatter")) return "scatter";
            if (txt.Contains("area")) return "area";
            if (txt.Contains("trend") || txt.Contains("time")) return "line";
            return "bar";
        }

        public static (string X, string Y, string? Series) Infer(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string t)
        {
            var f = rows.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal).ToArray();
            if (f.Length == 0) return ("category", "value", null);
            var x = f.FirstOrDefault(k => rows.Any(r => IsCat(r[k]))) ?? f[0];
            var y = f.FirstOrDefault(k => rows.Any(r => IsNum(r[k]))) ?? f[0];
            var s = f.FirstOrDefault(k => k != x && k != y && rows.Any(r => IsCat(r[k])));
            return (x, y, s);
        }

        public static IReadOnlyList<Dictionary<string, object?>> Parse(string j)
        {
            if (string.IsNullOrWhiteSpace(j)) return Array.Empty<Dictionary<string, object?>>();
            using var d = JsonDocument.Parse(j);
            var e = d.RootElement;
            if (e.ValueKind == JsonValueKind.Array) return e.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(ToR).ToArray();
            return new[] { ToR(e) };
        }

        private static Dictionary<string, object?> ToR(JsonElement e)
        {
            var r = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) r[p.Name] = ToV(p.Value);
            return r;
        }

        private static object? ToV(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetInt64(out var i) => i,
            JsonValueKind.Number when e.TryGetDouble(out var d) => d,
            JsonValueKind.String => e.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        private static bool IsCat(object? v) => v is string s && !IsNum(s);
        private static bool IsNum(object? v) => v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal || (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    }
}
