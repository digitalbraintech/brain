using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Core;

internal static class DurableStateJson
{
    internal static IJsonTypeInfoResolver TypeInfoResolver { get; } =
        new DefaultJsonTypeInfoResolver();
}
