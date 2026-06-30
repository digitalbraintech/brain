using DigitalBrain.Core;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

public static class SynapseStream
{
    public const string ProviderName = "DigitalBrainTimeline";

    public static IAsyncStream<Synapse> Timeline(this IStreamProvider provider) =>
        provider.GetStream<Synapse>(StreamId.Create("timeline", "global"));
}
