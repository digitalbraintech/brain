using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DigitalBrain.Abstractions.Signals;

// A signal is a type name plus a JSON body. Core ships no vocabulary; callers invent it.
[GenerateSerializer]
[Alias("db.signal")]
public sealed partial record Signal
{
    public const int MaxBodyBytes = 65_536;
    public const int MaxTypeLength = 64;

    private Signal(string type, string body)
    {
        Type = type;
        Body = body;
    }

    [Id(0)] public string Type { get; }

    [Id(1)] public string Body { get; }

    public static Signal Create(string type, string body)
    {
        if (string.IsNullOrWhiteSpace(type) || !TypeName().IsMatch(type))
        {
            throw new SignalRejectedException(
                $"Signal type '{type}' is not vocabulary. Use letters only, such as 'Note'; put identity in the neuron name.");
        }

        body = string.IsNullOrWhiteSpace(body) ? "{}" : body;
        var bytes = Encoding.UTF8.GetByteCount(body);
        if (bytes > MaxBodyBytes)
        {
            throw new SignalRejectedException(
                $"Signal body is {bytes / 1024} KB; the limit is {MaxBodyBytes / 1024} KB. "
                + "Split the content across neurons or store it externally and fire a reference.");
        }

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            throw new SignalRejectedException($"Signal body is not valid JSON: {error.Message}");
        }

        return new Signal(type, body);
    }

    [GeneratedRegex("^[A-Za-z]{1,64}$")]
    private static partial Regex TypeName();
}

public sealed class SignalRejectedException(string message) : InvalidOperationException(message);
