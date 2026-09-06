// Typed handler: UserMessaged -> chart + desk scene. Subscribe it to IComposer inbox.
if (Signal is not UserMessaged message) return;
var chart = Brain.GetEntity<IChart>("main-my-messages");
await chart.Render(new ChartState("Messages I sent", "line", Array.Empty<ChartPoint>()));
await chart.Append(new ChartPoint(message.Text, 1, EventId: message.CommandId.ToString()), "Messages I sent");
await Brain.GetEntity<ISurface>("desk").Open(
    new SurfaceScene("chart:main:my-messages", "Messages I sent"),
    8);
