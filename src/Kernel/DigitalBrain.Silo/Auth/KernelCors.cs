namespace DigitalBrain.Kernel.Auth;

/// <summary>
/// Cross-origin access for the deployed shell, which is served from a Static Web App
/// origin distinct from the kernel's. Inactive unless an origin is configured, so
/// same-origin and local hosting keep today's behavior.
/// </summary>
internal static class KernelCors
{
    public const string AllowedOriginConfigurationKey = "DigitalBrain:Cors:AllowedOrigin";

    private const string PolicyName = "shell";

    public static IHostApplicationBuilder AddKernelCors(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (ResolveOrigin(builder.Configuration) is not { } origin)
        {
            return builder;
        }

        builder.Services.AddCors(options => options.AddPolicy(
            PolicyName,
            policy => policy
                .WithOrigins(origin)
                .AllowAnyHeader()
                .AllowAnyMethod()));

        return builder;
    }

    public static WebApplication UseKernelCors(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (ResolveOrigin(app.Configuration) is not null)
        {
            // Ahead of the auth gate so a 401 still carries the CORS headers the
            // browser needs to surface it as a status rather than a network error.
            app.UseCors(PolicyName);
        }

        return app;
    }

    private static string? ResolveOrigin(IConfiguration configuration)
        => configuration[AllowedOriginConfigurationKey] is { Length: > 0 } origin
            ? origin.TrimEnd('/')
            : null;
}
