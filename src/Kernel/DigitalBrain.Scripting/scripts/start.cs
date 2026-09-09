#:project ../../DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
#:project ../../../Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj
#:property TargetFramework=net11.0
#:property PublishAot=false

using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.UI;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Application("start");
var activities = app.Script("activities", "activities.cs");
var ui = app.Script("ui", "ui.cs");
var composer = brain.Get<IComposer>(IComposer.DefaultInstanceName);
var assistant = brain.Get<IAssistant>("assistant");
app.Connect("composer-to-assistant", composer.Events().UserMessaged, assistant.Inputs().UserMessaged);
app.OnApply(async (run, ct) =>
{
    await run.ApplyScriptAsync(activities, ct);
    await run.ApplyScriptAsync(ui, ct);
    await run.EnsureInitializedAsync(ct);
});
await app.RunAsync(args);
