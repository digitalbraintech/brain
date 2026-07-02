// brain.cs - thin C# for "dotnet run brain.cs" (fast packed launcher).
// Spins local DigitalBrain as Aspire project.
// JUST uses IAspireNeuron (via client or inside) to start.
// Integrations (Telegram bot, Flutter) are packed NeuroPacks from marketplace - NO logic inside this .cs .
// The pack provides the Aspire resource (using extensions like AddFlutterClient / AddDefaultDevFlutterClient).
// Telegram opt-in via --telegram arg (pack model); Flutter Windows client is dev default.

using Aspire.Hosting.DigitalBrain;
using System;
using System.Linq;

var builder = DistributedApplication.CreateBuilder(args);

var ctx = builder.AddDigitalBrain("digitalbrain");

// Service-to-service secret gating the secrets-returning GetPackConfig RPC and the internal-only generic Send.
// Shared (same value) between the kernel and any internal transport; auto-generated when absent. Must be wired
// the same way as AppHost.cs, or an internal transport's pull/forward is denied outside Development.
var internalServiceKey = builder.AddParameter(
    "internal-service-key",
    () => builder.Configuration["Parameters:internal-service-key"] ?? Guid.NewGuid().ToString("N"),
    secret: true);

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);
kernel.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);

// Packed integrations added via their Aspire extensions from the marketplace pack's SDK.
// No logic here for the bot or Flutter - just declare the resource from the pack (dev default Flutter via helper).
bool withTelegram = args.Any(a => a.Contains("telegram", StringComparison.OrdinalIgnoreCase));

if (withTelegram)
{
    // Real transport resource (the echo placeholder is gone). Optional secret token -> no-op without it.
    var telegramBotToken = builder.AddParameter(
        "telegram-bot-token",
        () => builder.Configuration["Parameters:telegram-bot-token"] ?? string.Empty,
        secret: true);
    var telegramTransport = builder.AddProject<Projects.DigitalBrain_Telegram_Transport>("telegram-bot");
    ctx.WireTelegramTransport(telegramTransport, kernel, telegramBotToken, internalServiceKey);
}

// Always default Windows thin client (UI full impl stays marketplace NeuroPack). Uses extracted dev helper.
_ = ctx.AddDefaultDevFlutterClient(kernel); // null ok: no client if path missing (rare in dev tree)

builder.Build().Run();

// At runtime, IAspireNeuron can be used (e.g. from start or client) to start/orchestrate the project with the packed.