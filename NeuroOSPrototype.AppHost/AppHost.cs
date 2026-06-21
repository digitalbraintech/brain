using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
})
.WithOrleansDashboard(8080)   // Encapsulated live cluster UI (like standalone MCP for observability)
.WithMcp();                   // For runtime MCP connect + self-improvement (SystemStatusNeuron uses it)

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(ctx.Orleans)
    .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
    .WithReplicas(ctx.KernelReplicas)
    // Expose Orleans Dashboard standalone (fast live view of grains, activations, marketplace)
    .WithEndpoint(name: "orleans-dashboard", port: ctx.OrleansDashboardPort ?? 8080, isProxied: false);

var tui = builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(ctx.OrleansClient);

silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
if (ctx.EnableOrleansDashboard)
{
    silo.WithEnvironment("ORLEANS_DASHBOARD_PORT", (ctx.OrleansDashboardPort ?? 8080).ToString());
}

builder.Build().Run();