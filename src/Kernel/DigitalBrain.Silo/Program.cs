using DigitalBrain.Aspire;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using ModelContextProtocol.AspNetCore;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddKernelCors();
builder.Services.AddDigitalBrainMcp()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);

var app = builder.Build();

app.UseKernelCors();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapDigitalBrainMcp("/mcp");

app.Run();
