using DigitalBrain.Abstractions;
using DigitalBrain.Product.Interactions;
using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.UI;

public sealed class UIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IUserActionContinuation, ChatUserActionContinuation>();

        if (string.Equals(
                builder.Configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            builder.Services.TryAddSingleton<IKitImageStore, MemoryKitImageStore>();
        }
        else
        {
            builder.Services.TryAddSingleton<IKitImageStore, BlobKitImageStore>();
        }

        // GetService (nullable) is the honesty gate: generate_image only appears once an
        // IImageGeneration provider is actually configured (Task 6).
        builder.Services.AddSingleton<IAgentToolSource>(sp => new KitToolSource(
            sp.GetRequiredService<IGrainFactory>(),
            sp.GetService<IImageGeneration>(),
            sp.GetRequiredService<IKitImageStore>()));
        builder.Services.AddTransient<IWorkspaceInject, WorkspaceInject>();
    }
}
