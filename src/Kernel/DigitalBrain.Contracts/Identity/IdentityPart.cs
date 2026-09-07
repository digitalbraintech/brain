namespace DigitalBrain.Abstractions.Identity;

internal static class IdentityPart
{
    internal const int MaxLength = 128;

    // Neuron names and types: no whitespace, no ':' (it separates type from name), at most 128 chars.
    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaxLength)
        {
            throw new ArgumentException($"'{value[..16]}…' is longer than {MaxLength} characters.", parameterName);
        }
        if (value.Contains(':', StringComparison.Ordinal) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"'{value}' cannot contain ':' or whitespace.", parameterName);
        }
        return value;
    }
}
