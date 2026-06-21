using Orleans.Journaling;

var builder = Host.CreateApplicationBuilder(args);

builder.AddKeyedRedisClient("redis");

builder.UseOrleans(siloBuilder =>
{
    // Orleans Journalling alpha (DurableGrain + IDurable* collections for synapse timelines)
    // Use in-memory list for prototype (no Redis journal storage provider in current alpha; replace with proper provider for durable across restarts)
    siloBuilder.ConfigureServices(services =>
    {
        services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("journal", (_, _) => new DigitalBrain.Silo.InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
        services.AddSingleton<Orleans.Journaling.IJournaledStateManager, DigitalBrain.Silo.TestJournaledStateManager>();
    });
});

var host = builder.Build();
host.Run();
