using DigitalBrain.Abstractions;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainClientHostingExtensions
{
    public const string DefaultOwner = DigitalBrainNames.DefaultOwner;

    public static string OwnerConfigurationKey => DigitalBrainNames.Owner;

    public static string ClusteringConnectionName => DigitalBrainNames.Clustering;

    public static string RequireStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clustering = configuration.GetConnectionString(DigitalBrainNames.Clustering);
        if (string.IsNullOrWhiteSpace(clustering))
        {
            throw new InvalidOperationException(
                "No 'ConnectionStrings:clustering' is configured. Pass "
                + "--ConnectionStrings:clustering \"<azure storage connection>\" (the value the "
                + "running brain's silo uses; see the Aspire dashboard resource environment) or "
                + "export it as ConnectionStrings__clustering.");
        }

        return clustering;
    }

    public static string ResolveOwner(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var owner = configuration[DigitalBrainNames.Owner];
        return string.IsNullOrWhiteSpace(owner) ? DefaultOwner : owner;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(
        this IHostApplicationBuilder builder,
        Action<IClientBuilder>? configure = null,
        bool activateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return AddDigitalBrainClient(builder, ResolveOwner(builder.Configuration), configure, activateOnStart);
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(
        this IHostApplicationBuilder builder,
        string owner,
        Action<IClientBuilder>? configure = null,
        bool activateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.UseOrleansClient(client =>
        {
            Core.ModelPayloadSerialization.AddModelPayloadSerialization(client.Services);
            configure?.Invoke(client);
        });

        builder.Services.AddDigitalBrainOwner(
            builder.Configuration,
            owner,
            activateOnStart: activateOnStart);
        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainOwner(
        this IHostApplicationBuilder builder,
        string? owner = null,
        bool activateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddDigitalBrainOwner(builder.Configuration, owner, activateOnStart);
        return builder;
    }

    public static IServiceCollection AddDigitalBrainOwner(
        this IServiceCollection services,
        IConfiguration configuration,
        string? owner = null,
        bool activateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var resolvedOwner = string.IsNullOrWhiteSpace(owner)
            ? ResolveOwner(configuration)
            : owner!;
        services.AddSingleton<IDigitalBrain>(
            serviceProvider => DigitalBrainClient.Connect(
                serviceProvider.GetRequiredService<IGrainFactory>(),
                resolvedOwner));
        if (activateOnStart)
        {
            services.AddHostedService<DigitalBrainActivationHostedService>();
        }

        return services;
    }
}
