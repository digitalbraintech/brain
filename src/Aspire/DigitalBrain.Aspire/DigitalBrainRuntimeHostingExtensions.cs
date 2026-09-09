using Azure.Data.Tables;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Storage;

namespace DigitalBrain.Aspire;

public static class DigitalBrainRuntimeHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrain(
        this IHostApplicationBuilder builder,
        ModuleManifest modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Reminders);
        // AppHost's WithGrainStorage(DefaultGrainStorage, grainState) auto-wires the "Default"
        // provider through Orleans' own config-driven discovery, which resolves its
        // BlobServiceClient via GetRequiredKeyedService<BlobServiceClient>("grainstate") — so the
        // keyed client below is the only piece the runtime needs to supply. Setting
        // AzureBlobStorageOptions.BlobServiceClient must be registered before Orleans applies
        // does not work here: the auto-wired provider's own Configure delegate runs afterward and
        // unconditionally overwrites it, throwing when no keyed client is registered.
        builder.AddKeyedAzureBlobServiceClient(DigitalBrainNames.GrainState);
        builder.UseOrleans(silo =>
        {
            ConfigureStandaloneAzureClustering(silo, builder.Configuration);
            silo.Services.AddKeyedSingleton<IGrainStorageSerializer>(
                DigitalBrainNames.DefaultGrainStorage,
                static (services, _) => new OrleansGrainStorageSerializer(
                    services.GetRequiredService<Orleans.Serialization.Serializer>()));
            silo.Services.AddOptions<AzureBlobStorageOptions>(DigitalBrainNames.DefaultGrainStorage)
                .Configure(static options => options.ContainerName = "digitalbrain-v2-state");
            silo.AddAzureBlobJournal(builder.Configuration);
            DigitalBrainRuntime.Add(silo, modules);
            silo.AddDashboard(options =>
            {
                options.CounterUpdateIntervalMs = 5000;
                options.HistoryLength = 200;
            });
        });
        builder.AddDigitalBrainOwner(activateOnStart: false);
        return builder;
    }

    // Aspire AppHost (and some Container App configs) inject Orleans:Clustering:ProviderType.
    // Only wire Azure membership/reminders from ConnectionStrings when that provider is absent,
    // otherwise Orleans ends up with a duplicate IMembershipTable registration.
    private static void ConfigureStandaloneAzureClustering(ISiloBuilder silo, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Orleans:Clustering:ProviderType"]))
        {
            return;
        }

        var clustering = configuration.GetConnectionString(DigitalBrainNames.Clustering);
        if (string.IsNullOrWhiteSpace(clustering))
        {
            if (configuration.GetValue("DigitalBrain:Standalone", false))
            {
                throw new InvalidOperationException(
                    $"Missing connection string '{DigitalBrainNames.Clustering}'. "
                    + "Standalone hosts require Azure Table clustering (or Orleans:Clustering:ProviderType).");
            }

            return;
        }

        silo.UseAzureStorageClustering(options =>
            options.TableServiceClient = new TableServiceClient(clustering));

        var reminders = configuration.GetConnectionString(DigitalBrainNames.Reminders)
            ?? clustering;
        silo.UseAzureTableReminderService(reminders);
    }
}
