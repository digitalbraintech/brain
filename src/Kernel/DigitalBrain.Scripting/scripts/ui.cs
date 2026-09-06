async Task CreateUI()
{
    // Execution observations arrive on a separate durable turn, so this renderer
    // can subscribe before opening itself without a callback into its busy handler.
    var activities = Brain.Get<IActivities>(IActivities.DefaultInstanceName);
    var renderer = Brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
    await renderer.SubscribeToAsync<IUIRenderer, IActivities, ActivityChanged>(activities.Id, CancellationToken);

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
    await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "One brain", home), CancellationToken);
}
