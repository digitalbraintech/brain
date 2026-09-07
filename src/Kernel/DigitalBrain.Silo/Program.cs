using DigitalBrain.Aspire;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddKernelCors();

var app = builder.Build();
app.UseKernelCors();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.Run();
