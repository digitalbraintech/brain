using Newtonsoft.Json.Serialization;

namespace DigitalBrain.Execution;

// Entity snapshots written before contracts moved out of the kernel contain CLR
// names in $type metadata. Keep those snapshots readable; all new writes use the
// current names. This only translates the known execution-context value types.
internal sealed class ExecutionContextSerializationBinder(ISerializationBinder? inner) : ISerializationBinder
{
    private const string LegacyAssembly = "DigitalBrain.Abstractions";
    private const string LegacyNamespace = "DigitalBrain.Abstractions.Execution.";
    private static readonly Type[] ContextTypes =
    [
        typeof(ExecutionContextState), typeof(ExecutionId), typeof(ContextSlot),
        typeof(ContextPath), typeof(ContextEntry), typeof(ContextDigest),
    ];
    private readonly ISerializationBinder _inner = inner ?? new DefaultSerializationBinder();

    public Type BindToType(string? assemblyName, string typeName)
    {
        foreach (var type in ContextTypes)
        {
            var legacyName = LegacyNamespace + type.Name;
            if (typeName == legacyName && IsLegacyAssembly(assemblyName))
            {
                return type;
            }
            if (typeName == legacyName + "[]" && IsLegacyAssembly(assemblyName))
            {
                return type.MakeArrayType();
            }

            // Collection metadata embeds its element's assembly-qualified name.
            typeName = typeName.Replace(
                legacyName + ", " + LegacyAssembly,
                type.FullName + ", " + type.Assembly.GetName().Name,
                StringComparison.Ordinal);
        }

        return _inner.BindToType(assemblyName, typeName);
    }

    public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        => _inner.BindToName(serializedType, out assemblyName, out typeName);

    private static bool IsLegacyAssembly(string? name)
        => name == LegacyAssembly || name?.StartsWith(LegacyAssembly + ",", StringComparison.Ordinal) == true;
}
