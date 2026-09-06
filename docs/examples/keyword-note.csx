// Algorithm:
//   inbox broadcasts UserMessaged
//   this behavior reacts when the text starts with "note:"
//   then broadcasts Note with the remainder
// Subscribe: IComposer inbox --UserMessaged--> this behavior
if (Signal is not UserMessaged message) return;
const string prefix = "note:";
if (!message.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
var text = message.Text[prefix.Length..].Trim();
if (string.IsNullOrWhiteSpace(text)) return;
await Brain.PublishAsync(new Note(text));
