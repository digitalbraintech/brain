var builder = Host.CreateApplicationBuilder(args);

builder.AddKeyedRedisClient("redis");

builder.UseOrleans();

var host = builder.Build();
host.Run();
