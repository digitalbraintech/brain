// Typed handler: Note -> Note. Subscribe it to any source that broadcasts Note.
if (Signal is not Note note) return;
await Brain.PublishAsync(new Note($"echo: {note.Text}"));
