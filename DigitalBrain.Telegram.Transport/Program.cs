using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Telegram.Transport;
using Microsoft.Extensions.Options;
using Telegram.BotAPI.GettingUpdates;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramTransportOptions>(builder.Configuration.GetSection("Telegram"));
// The internal service key lives under the shared DigitalBrain section (same key the kernel reads), not the
// transport's Telegram section — bind it onto the options explicitly so the GetPackConfig pull can present it.
builder.Services.Configure<TelegramTransportOptions>(o =>
    o.InternalServiceKey = builder.Configuration["DigitalBrain:InternalServiceKey"] ?? o.InternalServiceKey);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TelegramTransportOptions>>().Value);
builder.Services.AddHttpClient();

builder.Services.AddSingleton<TelegramBotAccessor>();
builder.Services.AddSingleton<TelegramWebhookSetup>();
builder.Services.AddSingleton<TelegramUpdateForwarder>();
builder.Services.AddSingleton<TelegramReplyDispatcher>();

// gRPC client to the brain gateway. "https+http://gateway" resolves via Aspire
// service discovery from the gateway resource's endpoints injected by the host.
var gatewayAddress = builder.Configuration["DigitalBrain:GatewayAddress"] ?? "https+http://gateway";
builder.Services.AddGrpcClient<DigitalBrainGateway.DigitalBrainGatewayClient>(o =>
{
    o.Address = new Uri(gatewayAddress);
});

builder.Services.AddHostedService<SynapseStreamConsumer>();

var app = builder.Build();

// Telegram POSTs Update payloads here. The optional secret-token header is
// checked when WebhookSecretToken is configured. Forwarding is fire-and-forget
// so the POST is acked fast; the brain round-trip happens off the request path.
app.MapPost("/webhook", async (
    HttpContext context,
    TelegramUpdateForwarder forwarder,
    IOptions<TelegramTransportOptions> options,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var secret = options.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var header = context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!string.Equals(header, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    Update? update;
    try
    {
        update = await context.Request.ReadFromJsonAsync<Update>(ct);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to deserialize Telegram update.");
        return Results.Ok();
    }

    if (update is null)
        return Results.Ok();

    await forwarder.ForwardAsync(update, ct);
    return Results.Ok();
});

app.MapGet("/health", () => Results.Ok(new { ok = true }));

await app.RunAsync();

// Exposed so WebApplicationFactory<Program> can host the transport in-process for the contract tests.
public partial class Program;
