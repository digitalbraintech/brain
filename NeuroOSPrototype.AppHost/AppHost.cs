var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("neuro")
    .WithClustering(redis)
    .WithGrainStorage("Default", redis);

builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(orleans);

builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(orleans.AsClient());  // CLI can act as client to fire synapses to neurons

builder.Build().Run();
