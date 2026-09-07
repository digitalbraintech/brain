namespace DigitalBrain.Abstractions.Scripting;

public static class ApplicationContracts
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, string> Registered = new()
    {
        [typeof(string)] = "system.string/v1",
        [typeof(bool)] = "system.boolean/v1",
        [typeof(int)] = "system.int32/v1",
        [typeof(long)] = "system.int64/v1",
        [typeof(DigitalBrain.Chat.UserMessaged)] = "chat.user-messaged/v1",
        [typeof(DigitalBrain.Abstractions.Signals.DigitalBrainActivated)] = "brain.activated/v1",
    };
    private static readonly Dictionary<string, Type> TypesByWireName = Registered
        .ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);

    public static void RegisterJson<T>(string stableName, int schemaVersion)
        => RegisterJson(typeof(T), stableName, schemaVersion);

    private static void RegisterJson(Type type, string stableName, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableName);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        var wireName = $"{stableName}/v{schemaVersion}";
        lock (Gate)
        {
            if (Registered.TryGetValue(type, out var existing)
                && !string.Equals(existing, wireName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{type}' is already registered as application contract '{existing}'.");
            }
            if (TypesByWireName.TryGetValue(wireName, out var existingType)
                && existingType != type)
            {
                throw new InvalidOperationException(
                    $"Application contract '{wireName}' is already registered to '{existingType}'.");
            }
            Registered[type] = wireName;
            TypesByWireName[wireName] = type;
        }
    }

    internal static string NameFor(Type type)
    {
        lock (Gate)
        {
            if (Registered.TryGetValue(type, out var name)) { return name; }
        }
        if (type.GetCustomAttributes(typeof(ApplicationJsonContractAttribute), inherit: false)
                .SingleOrDefault() is ApplicationJsonContractAttribute declared)
        {
            RegisterJson(type, declared.StableName, declared.SchemaVersion);
            lock (Gate) { return Registered[type]; }
        }
        throw new InvalidOperationException(
            $"'{type}' has no registered application codec and cannot cross an application boundary.");
    }
}
