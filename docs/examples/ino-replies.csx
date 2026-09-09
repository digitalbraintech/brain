// Algorithm:
//   Ino broadcasts Responded after a UserMessaged turn
//   this behavior reacts and appends a point to IChart "ino-replies"
// Subscribe: IAssistant assistant --Responded--> this behavior
if (Signal is not Responded reply) return;
var label = reply.Text.Length <= 48 ? reply.Text : reply.Text[..48];
var chart = Brain.GetEntity<IChart>("ino-replies");
await chart.Append(new ChartPoint(label, 1, EventId: reply.CommandId.ToString()), "Ino replies");
await Brain.GetEntity<ISurface>("desk").Open(
    new SurfaceScene("chart:ino-replies", "Ino replies"),
    8);
