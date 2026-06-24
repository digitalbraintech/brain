using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace DigitalBrain.Silo;

public static class ContextServices
{
    // Registers the document-RAG vector store + ingestor. Qdrant when an endpoint is configured
    // (DigitalBrain:Qdrant:Endpoint or the "qdrant" connection string / Aspire AddQdrant), else in-memory.
    public static IServiceCollection AddContextStore(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["DigitalBrain:Qdrant:Endpoint"] ?? configuration.GetConnectionString("qdrant");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddSingleton(new QdrantClient(new Uri(endpoint)));
            services.AddSingleton<IVectorStore, QdrantVectorStore>();
        }
        else
        {
            services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        }

        services.AddSingleton<DocumentIngestor>();
        return services;
    }
}
