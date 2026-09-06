var ino = Brain.Get<IAssistant>("assistant");
var inbox = Brain.Get<IComposer>(IComposer.DefaultInstanceName);
await ino.SubscribeToAsync<IComposer, UserMessaged>(inbox.Id);

var plotter = Brain.Get<IBehavior>("message-chart");
await plotter.SaveScriptAsync<UserMessaged, Note>("""
    if (Signal is not UserMessaged message) return;
    var chart = Brain.GetEntity<IChart>("main-my-messages");
    await chart.Render(new ChartState("Messages I sent", "line", Array.Empty<ChartPoint>()));
    await chart.Append(new ChartPoint(message.Text, 1, EventId: message.CommandId.ToString()), "Messages I sent");
    await Brain.GetEntity<ISurface>("desk").Open(
        new SurfaceScene("chart:main:my-messages", "Messages I sent"),
        8);
    """, DigitalBrain.Abstractions.Signals.BehaviorInputPolicy.EveryEvent);
await plotter.ActivateAsync();
await plotter.SubscribeToAsync<IComposer, UserMessaged>(inbox.Id);

await ino.RequestAsync(new AgentRequest(
    "Greet the owner in one short friendly sentence. Do not use tools."));
return $"Wired Ino ← inbox and message-chart for owner '{Brain.Owner.Value}'.";

