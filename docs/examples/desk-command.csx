// Algorithm:
//   inbox broadcasts UserMessaged
//   this behavior reacts when the text starts with "desk:"
//   then opens an ISurface scene on the shared desk
// Subscribe: IComposer inbox --UserMessaged--> this behavior
if (Signal is not UserMessaged message) return;
const string prefix = "desk:";
if (!message.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
var title = message.Text[prefix.Length..].Trim();
if (string.IsNullOrWhiteSpace(title)) title = "Desk note";
await Brain.GetEntity<ISurface>("desk").Open(
    new SurfaceScene($"note:{message.CommandId}", title),
    8);
