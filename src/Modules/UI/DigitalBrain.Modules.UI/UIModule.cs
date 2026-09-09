using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Interactions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Chat;
using DigitalBrain.Abstractions.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.UI;

public sealed class UIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IUserActionContinuation, ChatUserActionContinuation>();
        builder.Services.AddSingleton(new ApplicationNeuronEventRegistration(
            IComposer.GrainTypeName, "user-messaged", "chat.user-messaged/v1", typeof(UserMessaged)));
        builder.Services.AddSingleton(new ApplicationNeuronCapabilityRegistration(
            "uirenderer", typeof(IUIRenderer), "renderer", IUIRenderer.DefaultInstanceName,
            "src/Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj"));
        builder.Services.AddSingleton(new ApplicationNeuronInputRegistration(
            "uirenderer", "ui.open-surface/v1", typeof(OpenSurface), "open-surface", IsPublic: true));
        builder.Services.AddSingleton(new ApplicationNeuronEventRegistration(
            "uirenderer", "surface-opened", "ui.surface-opened/v1", typeof(SurfaceOpened), IsPublic: true));
        builder.Services.AddSingleton(new ApplicationNeuronEventRegistration(
            "uirenderer", "control-activated", "ui.control-activated/v1", typeof(ControlActivated), IsPublic: true));
        builder.Services.AddSingleton(new ApplicationNeuronEventRegistration(
            "uirenderer", "component-added", "ui.component-added/v1", typeof(ComponentAdded), IsPublic: true));
        builder.Services.AddSingleton(new ApplicationNeuronInputRegistration(
            "uirenderer", "activity.changed/v1", typeof(ActivityChanged)));

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
        builder.Services.AddSingleton<IApplicationScenarioDriver, WorkspaceChatScenarioDriver>();
    }
}
