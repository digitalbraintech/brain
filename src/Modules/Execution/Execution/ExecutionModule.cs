using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Execution;

public sealed class ExecutionModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.PostConfigure<OrleansJsonSerializerOptions>(options =>
            options.JsonSerializerSettings.SerializationBinder = new ExecutionContextSerializationBinder(
                options.JsonSerializerSettings.SerializationBinder));
    }
}
