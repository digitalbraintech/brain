using Azure.Storage.Blobs;
using DigitalBrain.Core.Config;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Config;

public static class PackConfigServices
{
    // Registers IPackConfigStore.
    // When BlobServiceClient is available (Aspire-hosted, multi-replica): DataProtection keys are persisted
    // to blob storage so all replicas share the same key ring. Without it (integration tests, fast path):
    // falls back to the default ephemeral/filesystem key ring (single-process only).
    public static IServiceCollection AddPackConfigStore(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName("DigitalBrain.PackConfig");

        // Register the DataProtection key persistence and blob backing as a deferred decision:
        // resolve BlobServiceClient only after the host is built, so missing it in test hosts
        // does not fail startup.
        services.AddSingleton<IPackConfigBackingStore>(sp =>
        {
            var blobs = sp.GetService<BlobServiceClient>();
            if (blobs is not null)
                return new AzureBlobPackConfigBackingStore(blobs);
            return new InMemoryPackConfigBackingStore();
        });

        services.AddSingleton<IPackConfigStore, PackConfigStore>();
        return services;
    }

    // Call this on the DataProtectionBuilder after BlobServiceClient is available to enable cluster-wide
    // shared key ring. Intended to be called from the Aspire-hosted path in Program.cs.
    public static void PersistPackConfigKeys(this IDataProtectionBuilder builder, BlobServiceClient blobs)
        => builder.PersistKeysToAzureBlobStorage(
            blobs.GetBlobContainerClient("pack-config").GetBlobClient("dp-keys/keys.xml"));
}
