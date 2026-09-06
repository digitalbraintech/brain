// Algorithm:
//   a source broadcasts Note
//   this behavior reacts and broadcasts Note ("echo: …")
// Subscribe: keyword-note (or any Note source) --Note--> this behavior
if (Signal is not Note note) return;
await Brain.PublishAsync(new Note($"echo: {note.Text}"));
