using DigitalBrain.Aspire;
using DigitalBrain.Scripting.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDigitalBrainClient(activateOnStart: false);
builder.Services.Configure<StartupScriptOptions>(
    builder.Configuration.GetSection(StartupScriptOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStartupActivationSource, DigitalBrainActivationSource>();
builder.Services.AddSingleton<IStartupScriptRunner, CSharpStartupScriptRunner>();
builder.Services.AddSingleton<IStartupExecutionLedger>(services =>
{
    var options = services.GetRequiredService<IOptions<StartupScriptOptions>>().Value;
    return new FileStartupExecutionLedger(options.StateDirectory);
});
builder.Services.AddHostedService<StartupScriptWorker>();
builder.Services.AddSingleton<BehaviorProgramRunner>();
builder.Services.AddHostedService<BehaviorExecutionWorker>();
await builder.Build().RunAsync();
