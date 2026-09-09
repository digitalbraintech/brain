using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Excel;

public sealed class ExcelModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IAgentToolSource>(
            static sp => new ExcelToolSource(sp.GetRequiredService<IGrainFactory>()));
    }
}
