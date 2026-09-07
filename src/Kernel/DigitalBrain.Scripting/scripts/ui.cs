#:project ../../DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
#:project ../../../Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj
#:property TargetFramework=net11.0
#:property PublishAot=false

using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Product.Identity;
using DigitalBrain.UI;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Application("ui");
var activities = brain.Get<IActivities>(IActivities.DefaultInstanceName);
var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
app.Connect("activities-to-renderer", activities.Events().ActivityChanged, renderer.Inputs().ActivityChanged);
app.OnApply(async (run, ct) =>
{
    var home = new SurfaceComponent("split", Properties: new Dictionary<string, string>
    {
        ["graphFraction"] = "0.61",
    }, Children:
    [
        new SurfaceComponent("brain-graph", "brain", new Dictionary<string, string>
        {
            ["scope"] = "selected",
            ["activitiesName"] = IActivities.DefaultInstanceName,
        },
        [
            new SurfaceComponent("activity-list", "activities", new Dictionary<string, string>
            {
                ["placement"] = "top-right",
                ["source"] = IActivities.DefaultInstanceName,
            }),
        ]),
        new SurfaceComponent("chat", "input", new Dictionary<string, string>
        {
            ["name"] = "main",
            ["voice"] = "true",
        }),
    ]);
    await run.SendAsync(renderer, new OpenSurface(CommandId.New(), "home", "One brain", home), ct);
});
await app.RunAsync(args);
