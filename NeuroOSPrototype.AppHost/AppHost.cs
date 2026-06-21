var builder = DistributedApplication.CreateBuilder(args);

// DigitalBrain setup - the SDK (DigitalBrain.Aspire) now has AddDigitalBrain that encapsulates the wiring of redis+orleans+ollama using options for model etc. (see DigitalBrainBuilderExtensions.cs).
// For compatibility in this AppHost we use explicit wiring with 3 replicas.

var redis = builder.AddRedis("redis");
var orleans = builder.AddOrleans("neuro")
    .WithClustering(redis)
    .WithGrainStorage("Default", redis);

var ollama = builder.AddOllama("ollama")
    .WithGPUSupport()
    .WithDataVolume();
var qwen = ollama.AddModel("qwen", "qwen2.5-coder:1.5b");

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(orleans)
    .WithReference(qwen)
    .WithReplicas(3);

var tui = builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(orleans.AsClient());

// Marketplace config for the system
silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", "true");

builder.Build().Run();

