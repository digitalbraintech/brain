namespace DigitalBrain.Abstractions;

// Single source of truth for the storage, clustering, reminder and journal resource names
// and the configuration keys shared by the Aspire hosting integration (AppHost side) and
// the silo runtime. The fabric is tables and blobs only: wire delivery rides Orleans grain
// calls, durability rides each neuron's journal blobs and grain state.
public static class DigitalBrainNames
{
    public const string Storage = "storage";
    public const string Clustering = "clustering";
    public const string Reminders = "reminders";
    public const string Journal = "journal";
    public const string GrainState = "grainstate";

    public const string JournalConnection = "journal";
    public const string DefaultGrainStorage = "Default";

    public const string Modules = "DigitalBrain:Modules";
}
