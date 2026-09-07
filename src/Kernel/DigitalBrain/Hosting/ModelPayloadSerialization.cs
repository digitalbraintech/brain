using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public static class ModelPayloadSerialization
{
    public static void AddModelPayloadSerialization(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSerializer(
            serializer => serializer.AddJsonSerializer(
                static type => type == typeof(System.Text.Json.JsonElement)));
    }
}
