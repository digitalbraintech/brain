using Azure.Storage.Blobs;
using DigitalBrain.Core.Config;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Config;

public static class PackConfigServices
{
    // Registers IPackConfigStore.
    // Pass blobsForKeyRing (Aspire-hosted path) to share the DataProtection key ring across all replicas via
    // blob storage — without it (integration tests, fast path) each process gets an ephemeral key ring.
    public static IServiceCollection AddPackConfigStore(
        this IServiceCollection services,
        BlobServiceClient? blobsForKeyRing = null)
    {
        var dp = services.AddDataProtection()
            .SetApplicationName("DigitalBrain.PackConfig");

        if (blobsForKeyRing is not null)
            dp.PersistKeysToAzureBlobStorage(
                blobsForKeyRing.GetBlobContainerClient("pack-config").GetBlobClient("dp-keys/keys.xml"));

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
}
