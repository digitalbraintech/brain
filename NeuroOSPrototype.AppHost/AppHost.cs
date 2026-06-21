using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
});

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(ctx.Orleans)
    .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
    .WithReplicas(ctx.KernelReplicas);

var tui = builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(ctx.OrleansClient);

silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");

builder.Build().Run();

