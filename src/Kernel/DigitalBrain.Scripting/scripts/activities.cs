#:project ../../DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
#:property TargetFramework=net11.0
#:property PublishAot=false

using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Application("activities");
var execution = brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
var activities = brain.Get<IActivities>(IActivities.DefaultInstanceName);
app.Connect("execution-to-activities", execution.Events().ExecutionChanged, activities.Inputs().ExecutionChanged);
await app.RunAsync(args);
