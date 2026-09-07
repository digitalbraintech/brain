using DigitalBrain.Aspire;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Auth;
using DigitalBrain.Sdk;
using DigitalBrain.Scripting.Definitions;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.Services.AddDefinitionAuthoring(
    builder.Configuration["DigitalBrain:DefinitionStore"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".digitalbrain", "v2", "definitions"));
builder.Services.AddAuthentication();
builder.AddKernelCors();
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));
builder.Services.AddTransient<IBrainGraphSource, BrainGraphSource>();
builder.Services.AddSingleton<BrainGraphMetadata>();
builder.Services.AddTransient<BrainGraphProjection>();
builder.Services.AddTransient<BrainGraphStream>();
builder.Services.AddTransient<IBrainGraphObservers, BrainGraphObservers>();

var app = builder.Build();
app.UseKernelCors();
// Module surfaces (browser OAuth callbacks) carry their own one-use request guards and must
// run before authentication and the Basic gate.
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseBasicAuthGate();
app.MapDefaultEndpoints();
app.MapOwnerCommands();
app.MapChatVoice();
app.MapChatStreams();
app.MapKitEntities();
app.MapSurfaceStreams();
app.MapSurfaceActivities();
app.MapSurfaceControls();
app.MapActivityResults();
app.MapActivities();
app.MapBrainGraph();
app.MapDefinitionStudio();
app.MapOrleansDashboard("/orleans");
app.Run();
