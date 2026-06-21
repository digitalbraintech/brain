var builder = DistributedApplication.CreateBuilder(args);

// DigitalBrain setup using options and resource concepts from DigitalBrain.Aspire SDK (the AddDigitalBrain in SDK now encapsulates the common wiring logic for redis + orleans + ollama based on options).
// For this AppHost we use explicit for compatibility, but 3 replicas of kernel as requested.

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

