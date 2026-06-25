using DigitalBrain.Core;
using System.Globalization;
using System.Text.Json;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

[GrainType("digitalbrain.data-visualization.v1")]
public class DataVisualizationNeuron : Neuron, IDataVisualizationNeuron
{
    public DataVisualizationNeuron(ILogger<DataVisualizationNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(VisualizeDataRequest request)
    {
        var requestId = !string.IsNullOrWhiteSpace(request.RequestId)
            ? request.RequestId!
            : request.CorrelationId ?? "chart-" + Guid.NewGuid().ToString("N")[..10];

        try
        {
            var surface = DataChartBuilder.BuildSurface(
                requestId,
                Self.Value,
                request.Prompt,
                request.DataJson,
                request.ChartHint);

            await FireAsync(new DataChartGenerated(requestId, surface));
        }
        catch (Exception ex)
        {
            await FireAsync(new DataChartFailed(requestId, ex.Message));
        }
    }
}

public static class DataChartBuilder
{
    private static readonly string[] RowArrayNames = ["rows", "data", "items", "values", "records"];
    private static readonly string[] ValidChartTypes = ["bar", "line", "area", "scatter", "pie"];

    public static UiSurface BuildSurface(
        string requestId,
        string emitter,
        string prompt,
        string dataJson,
        string? chartHint = null)
    {
        var rows = ParseRows(dataJson)
            .Select(row => (IReadOnlyDictionary<string, object?>)row)
            .ToArray();
        var chartType = ChooseChartType(prompt, chartHint);
        var (x, y, series) = InferEncoding(rows, chartType);
        var title = BuildTitle(prompt, chartType, x, y);
        var summary = $"{rows.Length} rows. {chartType} chart of {y} by {x}.";
        var color = series;

        var spec = new ChartSpec(
            Title: title,
            ChartType: chartType,
            Data: rows,
            X: x,
            Y: y,
            Series: series,
            Color: color,
            Tooltip: true,
            Crosshair: chartType != "pie",
            Summary: summary);

        return UiSurfaceSamples.DataChart(
            surfaceId: "surface.data-chart." + SanitizeId(requestId),
            emitter: emitter,
            spec: spec);
    }

    public static IReadOnlyList<Dictionary<string, object?>> ParseRows(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        using var document = JsonDocument.Parse(dataJson);
        return ParseRows(document.RootElement);
    }

    private static IReadOnlyList<Dictionary<string, object?>> ParseRows(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(ToRow)
                .ToArray();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        foreach (var name in RowArrayNames)
        {
            if (TryGetProperty(element, name, out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                return ParseRows(rows);
            }
        }

        return new[] { ToRow(element) };
    }

    private static Dictionary<string, object?> ToRow(JsonElement element)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            row[property.Name] = ToJsonFriendlyValue(property.Value);
        }

        return row;
    }

    private static object? ToJsonFriendlyValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };

    private static string ChooseChartType(string prompt, string? chartHint)
    {
        var hint = chartHint?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var direct = ValidChartTypes.FirstOrDefault(type => hint == type || hint.Contains(type, StringComparison.Ordinal));
            if (direct is not null)
            {
                return direct;
            }
        }

        var text = (chartHint + " " + prompt).ToLowerInvariant();
        if (text.Contains("pie", StringComparison.Ordinal) ||
            text.Contains("share", StringComparison.Ordinal) ||
            text.Contains("percent", StringComparison.Ordinal) ||
            text.Contains("percentage", StringComparison.Ordinal))
        {
            return "pie";
        }

        if (text.Contains("scatter", StringComparison.Ordinal) ||
            text.Contains("correlation", StringComparison.Ordinal))
        {
            return "scatter";
        }

        if (text.Contains("area", StringComparison.Ordinal))
        {
            return "area";
        }

        if (text.Contains("trend", StringComparison.Ordinal) ||
            text.Contains("over time", StringComparison.Ordinal) ||
            text.Contains("time series", StringComparison.Ordinal) ||
            text.Contains("timeseries", StringComparison.Ordinal))
        {
            return "line";
        }

        return "bar";
    }

    private static (string X, string Y, string? Series) InferEncoding(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string chartType)
    {
        var fields = rows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (fields.Length == 0)
        {
            return ("category", "value", null);
        }

        if (chartType == "scatter")
        {
            var numericFields = fields
                .Where(field => rows.Any(row => TryGet(row, field, out var value) && IsNumeric(value)))
                .ToArray();

            if (numericFields.Length >= 2)
            {
                var scatterSeries = fields.FirstOrDefault(field =>
                    field != numericFields[0] &&
                    field != numericFields[1] &&
                    rows.Any(row => TryGet(row, field, out var value) && IsCategorical(value)));

                return (numericFields[0], numericFields[1], scatterSeries);
            }
        }

        var x = fields.FirstOrDefault(field =>
            rows.Any(row => TryGet(row, field, out var value) && IsCategorical(value)));
        var y = fields.FirstOrDefault(field =>
            rows.Any(row => TryGet(row, field, out var value) && IsNumeric(value)));

        x ??= fields.FirstOrDefault(field => field != y) ?? fields[0];
        y ??= fields.FirstOrDefault(field => field != x) ?? fields[0];

        var series = fields.FirstOrDefault(field =>
            field != x &&
            field != y &&
            rows.Any(row => TryGet(row, field, out var value) && IsCategorical(value)));

        return (x, y, series);
    }

    private static bool TryGet(IReadOnlyDictionary<string, object?> row, string field, out object? value)
    {
        if (row.TryGetValue(field, out value))
        {
            return true;
        }

        foreach (var entry in row)
        {
            if (entry.Key.Equals(field, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsCategorical(object? value) =>
        value is string text && !IsNumeric(text);

    private static bool IsNumeric(object? value) =>
        value switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => true,
            string text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            _ => false
        };

    private static string BuildTitle(string prompt, string chartType, string x, string y)
    {
        var title = prompt.Trim();
        if (title.Length > 72)
        {
            title = title[..72].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(title)
            ? $"{ToTitleCase(chartType)} chart of {y} by {x}"
            : title;
    }

    private static string ToTitleCase(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string SanitizeId(string requestId)
    {
        var chars = requestId
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            .Take(64)
            .ToArray();

        return chars.Length == 0 ? "chart" : new string(chars);
    }
}

