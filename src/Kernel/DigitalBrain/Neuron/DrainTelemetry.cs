using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Core;

// The drain swallows reaction failures to keep the cursor; this is where they become visible.
internal static class DrainTelemetry
{
    private static readonly ActivitySource Source = new("DigitalBrain");

    internal static void Lost(ILogger? logger, NeuronId neuron, long sequence)
    {
        using var activity = Source.StartActivity("db.drain.lost");
        activity?.SetTag("neuron", neuron.ToString());
        activity?.SetTag("sequence", sequence);
        logger?.LogWarning(
            "Neuron {Neuron} never reacted to incoming sequence {Sequence}: it fell out of the retained window.",
            neuron,
            sequence);
    }

    internal static void Failed(ILogger? logger, NeuronId neuron, long sequence, Exception failure)
    {
        using var activity = Source.StartActivity("db.drain.failed");
        activity?.SetTag("neuron", neuron.ToString());
        activity?.SetTag("sequence", sequence);
        activity?.SetTag("exception.type", failure.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
        logger?.LogWarning(
            failure,
            "Neuron {Neuron} failed to react to incoming sequence {Sequence}; the cursor stays and the entry is retried.",
            neuron,
            sequence);
    }
}
