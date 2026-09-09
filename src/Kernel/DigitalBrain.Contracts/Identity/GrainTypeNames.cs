using System.Reflection;

namespace DigitalBrain.Abstractions.Identity;

internal static class GrainTypeNames
{
    internal static string Of(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        var named = contractType.GetField(
            "GrainTypeName",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (named is { IsLiteral: true } && named.GetRawConstantValue() is string grainTypeName
            && !string.IsNullOrWhiteSpace(grainTypeName))
        {
            return grainTypeName;
        }

        var declared = contractType.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType == typeof(GrainTypeAttribute))?
            .ConstructorArguments[0].Value as string;

        if (declared is not null)
        {
            return declared;
        }

        const string OrleansGrainSuffix = "Grain";
        var name = contractType.Name;

        if (contractType.IsInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            return name[1..];
        }

        return name.Length > OrleansGrainSuffix.Length && name.EndsWith(OrleansGrainSuffix, StringComparison.Ordinal)
            ? name[..^OrleansGrainSuffix.Length]
            : name;
    }
}
