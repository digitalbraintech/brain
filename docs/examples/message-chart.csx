// Algorithm:
//   inbox broadcasts UserMessaged
//   this behavior reacts and writes IChart + opens a desk scene
// Subscribe: IComposer inbox --UserMessaged--> this behavior
if (Signal is not UserMessaged message) return;
var chart = Brain.GetEntity<IChart>("main-my-messages");
await chart.Append(new ChartPoint(message.Text, 1, EventId: message.CommandId.ToString()), "Messages I sent");
await Brain.GetEntity<ISurface>("desk").Open(
    new SurfaceScene("chart:main:my-messages", "Messages I sent"),
    8);
