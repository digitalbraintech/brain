var ino = Brain.Get<IAssistant>("assistant");
var inbox = Brain.Get<IComposer>(IComposer.DefaultInstanceName);
await ino.SubscribeToAsync<IComposer, UserMessaged>(inbox.Id);
return $"Wired Ino ← inbox for owner '{Brain.Owner.Value}'.";

