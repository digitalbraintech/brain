namespace DigitalBrain.Abstractions.Identity;

internal static class IdentityPart
{
    internal const char OwnerNameSeparator = '/';

    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Contains(OwnerNameSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Identity parts cannot contain '{OwnerNameSeparator}' because it separates owner from name in grain keys.",
                parameterName);
        }

        if (value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Identity parts cannot contain whitespace.", parameterName);
        }

        return value;
    }
}
