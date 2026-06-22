using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

// Unified with fast start.cs path (memory kernel + surfaces) and full distributed here.
// See framework/start.cs for fast "dotnet run" INO + tasks + marketplace + UiSurfaces (Gmail etc).
// Experiences emit UiSurface (AuthButtonSurface etc) for sdk/flutter_demo + Telegram skeleton.
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
})
.WithOrleansDashboard(8080)
.WithMcp();

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(ctx.Orleans)
    .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
    .WithReplicas(ctx.KernelReplicas)
    .WithEndpoint(name: "orleans-dashboard", port: ctx.OrleansDashboardPort ?? 8080, isProxied: false);

var startUi = builder.AddProject<Projects.DigitalBrain_Cli>("start-ui")
    .WithReference(ctx.OrleansClient);

silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
silo.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");
if (ctx.EnableOrleansDashboard)
{
    silo.WithEnvironment("ORLEANS_DASHBOARD_PORT", (ctx.OrleansDashboardPort ?? 8080).ToString());
}

builder.Build().Run();