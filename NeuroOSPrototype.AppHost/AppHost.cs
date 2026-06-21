var builder = DistributedApplication.CreateBuilder(args);

// DigitalBrain Aspire resource (MVP per plan).
// In a consuming app you would do: builder.AddDigitalBrain("db").WithLLM()... 
// Here we wire directly (3 replicas of kernel) + comment the intended SDK usage.
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
    .WithReplicas(3);   // 3 replicas of kernel and OS

var tui = builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(orleans.AsClient());

// Marketplace config example
silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", "true");

builder.Build().Run();

// See DigitalBrain.Aspire for the resource + future fluent AddDigitalBrain / .WithLLM / .WithTUI / .AddExperience

