#load "activities.cs"
#load "ui.cs"

if (Input is not DigitalBrainActivated activated || activated.Owner != Brain.Owner)
    throw new InvalidOperationException("start.cs requires this brain's DigitalBrainActivated signal.");

await ConfigureActivities();
await CreateUI();
return $"Started activities and the programmable home for '{Brain.Owner.Value}'.";

