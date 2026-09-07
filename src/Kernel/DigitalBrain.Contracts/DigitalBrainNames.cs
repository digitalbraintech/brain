namespace DigitalBrain.Abstractions;

// Single source of truth for resource/connection names and configuration keys used by
// the Aspire hosting integration (AppHost side) and the silo/client runtime integration.
// The fabric is tables and blobs only — no Streams/PubSub: wire delivery rides Orleans
// grain calls and BroadcastChannel, durability rides each neuron's journal blobs.
public static class DigitalBrainNames
{
    public const string DefaultBrain = "brain";
    public const string DefaultOwner = "dev";

    public const string Storage = "storage";
    public const string Clustering = "clustering";
    public const string Reminders = "reminders";
    public const string Journal = "journal";
    public const string GrainState = "grainstate";

    public const string JournalConnection = "journal";
    public const string DefaultGrainStorage = "Default";

    // Orleans BroadcastChannel carrying DigitalBrainActivated fan-out. The channel key is the
    // subscriber's grain key ("{owner}/{name}"), so implicit channel subscribers activate as
    // regular owner-bound neurons.
    public const string BroadcastChannelProvider = "db.broadcast";

    public const string Owner = "DigitalBrain:Owner";
    public const string Modules = "DigitalBrain:Modules";
    public const string Mode = "DigitalBrain:Mode";
    public const string TestingMode = "Testing";
    public const string Fakes = "DigitalBrain:Fakes:Enabled";
}
