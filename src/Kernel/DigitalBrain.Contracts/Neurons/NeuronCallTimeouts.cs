namespace DigitalBrain.Abstractions.Neurons;

public static class NeuronCallTimeouts
{
    public const string LongRunning = "00:05:00";

    // Bounds auxiliary lookups (graph connections, brain routing, journal reads) so a
    // stuck grain call cannot hold its caller's turn forever.
    public static readonly TimeSpan LookupBound = TimeSpan.FromSeconds(40);
}
