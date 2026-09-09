using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DigitalBrain.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    private const string AlivePath = "/alive";
    private const string HealthPath = "/health";
    private const string TelemetryPrefix = "DigitalBrain";
    private const string AzuriteAccountPathSegment = "/devstoreaccount1/";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureOpenTelemetry(builder);
        AddDefaultHealthChecks(builder);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(HealthPath).AllowAnonymous();
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live"),
        }).AllowAnonymous();

        return app;
    }

    private static void AddDefaultHealthChecks<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
        => builder.Services
            .AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);

    private static void AddOpenTelemetryExporters<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    private static void ConfigureOpenTelemetry<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var configuredSampleRatio = builder.Configuration.GetValue<double?>("Telemetry:Tracing:SampleRatio");
        var sampleRatio = Math.Clamp(configuredSampleRatio ?? 1d, 0d, 1d);
        var rootSampler = new SuppressAzureStorageSampler(new TraceIdRatioBasedSampler(sampleRatio));

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        if (builder.Configuration.GetValue<bool?>("DigitalBrain:AI:Telemetry:EnableSensitiveData")
            ?? builder.Configuration.GetValue<bool?>("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT")
            ?? false)
        {
            // MEAI's structured inference events use this category. Broad Microsoft
            // Warning filters must not discard opted-in GenAI evidence. Tool payloads
            // belong to execute_tool spans; avoid duplicate Trace-level logging.
            builder.Logging.AddFilter("Microsoft.Extensions.AI.OpenTelemetryChatClient", LogLevel.Information);
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceNamespace: TelemetryPrefix))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TelemetryPrefix)
                .AddMeter($"{TelemetryPrefix}.*")
                .AddMeter("Experimental.Microsoft.Extensions.AI")
                .AddMeter("Experimental.Microsoft.Extensions.AI.*")
                .AddMeter("Microsoft.Extensions.AI")
                .AddMeter("Microsoft.Extensions.AI.*")
                .AddMeter("Microsoft.Orleans"))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(rootSampler))
                .AddProcessor(new SuppressAzureStorageActivityProcessor())
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(TelemetryPrefix)
                .AddSource($"{TelemetryPrefix}.*")
                .AddSource("Experimental.Microsoft.Extensions.AI")
                .AddSource("Experimental.Microsoft.Extensions.AI.*")
                .AddSource("Microsoft.Extensions.AI")
                .AddSource("Microsoft.Extensions.AI.*")
                .AddSource("Microsoft.Orleans.Application")
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase)
                        && !context.Request.Path.StartsWithSegments(AlivePath, StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation(options =>
                {
                    options.FilterHttpRequestMessage = static request => !IsAzuriteRequest(request);
                }));

        AddOpenTelemetryExporters(builder);
    }

    private static bool IsAzuriteRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        return path is not null
            && path.Contains(AzuriteAccountPathSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAzureStorageNoiseName(string name)
        => name.StartsWith("TableClient.", StringComparison.Ordinal)
            || name.StartsWith("TableServiceClient.", StringComparison.Ordinal)
            || name.StartsWith("BlobClient.", StringComparison.Ordinal)
            || name.StartsWith("BlobBaseClient.", StringComparison.Ordinal)
            || name.StartsWith("BlobContainerClient.", StringComparison.Ordinal)
            || name.StartsWith("BlobServiceClient.", StringComparison.Ordinal)
            || name.StartsWith("QueueClient.", StringComparison.Ordinal)
            || name.StartsWith("QueueServiceClient.", StringComparison.Ordinal);

    private static bool IsAzureStorageNoiseSource(string sourceName)
        => sourceName.StartsWith("Azure.Data.Tables", StringComparison.Ordinal)
            || sourceName.StartsWith("Azure.Storage", StringComparison.Ordinal)
            || string.Equals(sourceName, "Azure.Core.Http", StringComparison.Ordinal);

    private static bool IsAzureStorageNamespace(string? azNamespace)
        => azNamespace is "Microsoft.Tables"
            or "Microsoft.Storage"
            or "Microsoft.Blobs"
            or "Microsoft.Queue";

    private static bool IsAzureStorageNoise(Activity activity)
    {
        if (IsAzureStorageNoiseName(activity.OperationName)
            || IsAzureStorageNoiseSource(activity.Source.Name))
        {
            return true;
        }

        return activity.GetTagItem("az.namespace") is string azNamespace
            && IsAzureStorageNamespace(azNamespace);
    }

    private sealed class SuppressAzureStorageSampler(Sampler inner) : Sampler
    {
        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            if (IsAzureStorageNoiseName(samplingParameters.Name))
            {
                return new SamplingResult(SamplingDecision.Drop);
            }

            return inner.ShouldSample(in samplingParameters);
        }
    }

    private sealed class SuppressAzureStorageActivityProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            if (!IsAzureStorageNoise(activity))
            {
                return;
            }

            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            activity.IsAllDataRequested = false;
        }
    }
}
