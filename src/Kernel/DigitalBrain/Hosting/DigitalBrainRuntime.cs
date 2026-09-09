using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace DigitalBrain.Core;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainRuntime
{
    public static void Add(ISiloBuilder builder, ModuleManifest modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(DurableStateJson.TypeInfoResolver);
        // Awaited publish: a subscriber's failure surfaces to the publisher, matching the
        // direct-call delivery semantics of Send.
        builder.AddBroadcastChannel(
            DigitalBrainNames.BroadcastChannelProvider,
            options => options.FireAndForgetDelivery = false);
        ModelPayloadSerialization.AddModelPayloadSerialization(builder.Services);
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddSingleton(ApplicationWorkerCapabilityAuthority.Process);
        builder.Services.TryAddSingleton<SynapseOptions>();
        builder.Services.TryAddSingleton<SignalRouter>();
        builder.Services.TryAddSingleton<NeuronRuntime>();
        builder.Services.TryAddSingleton<IApplicationChatIngress, ApplicationChatIngress>();
        builder.Services.TryAddSingleton<IApplicationContractIngress, ApplicationContractIngress>();
        builder.Services.AddSingleton(new ApplicationNeuronEventRegistration(
            IActivitySource.GrainTypeName, "execution-changed", "activity.execution-changed/v1",
            typeof(ActivityExecutionChanged)));
        builder.Services.AddSingleton(new ApplicationNeuronEventRegistration(
            IActivities.GrainTypeName, "activity-changed", "activity.changed/v1", typeof(ActivityChanged)));
        builder.Services.AddSingleton(new ApplicationNeuronInputRegistration(
            IActivities.GrainTypeName, "activity.execution-changed/v1", typeof(ActivityExecutionChanged)));
        builder.AddIncomingGrainCallFilter<NeuronMembraneFilter>();

        foreach (var hook in ModuleHooksOf(modules))
        {
            hook.Configure(builder);
        }
    }

    private static IEnumerable<IModule> ModuleHooksOf(ModuleManifest modules)
        => modules.Types.Select(static type =>
        {
            if (type is not { IsClass: true, IsAbstract: false }
                || !typeof(IModule).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Configured module '{type.FullName}' must be a concrete {nameof(IModule)} with a public parameterless constructor.");
            }

            return (IModule)Activator.CreateInstance(type)!;
        });
}
