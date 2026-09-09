using DigitalBrain.Abstractions;
using Microsoft.Extensions.Configuration;
using Orleans.Journaling;

namespace DigitalBrain.Aspire;

/// <summary>
/// Azure-specific Orleans journal registration for the product host.
/// Core owns the journal model; this adapter owns how production storage is wired.
/// </summary>
internal static class AzureOrleansJournalHosting
{
    internal static ISiloBuilder AddAzureBlobJournal(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(DigitalBrainNames.JournalConnection)
            ?? throw new InvalidOperationException(
                $"Missing connection string '{DigitalBrainNames.JournalConnection}'. "
                + "Neuron journals require Azure Blob storage in this host.");

        return builder.AddAzureBlobJournalStorage(options =>
        {
            options.ContainerName = "digitalbrain-v2-journal";
            options.ConfigureBlobServiceClient(connectionString);
        });
    }
}
