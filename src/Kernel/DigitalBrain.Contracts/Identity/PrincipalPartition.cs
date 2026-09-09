namespace DigitalBrain.Abstractions.Identity;

// Neuron names cannot contain '/' (owner/name separator). Per-principal resources
// use "{principal:N}.{local}" so chat, chart, graph, and registry isolate by principal.
public static class PrincipalPartition
{
    public static string InstanceName(PrincipalId principal, string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);

        var local = localName.Trim();
        if (local.Contains('/', StringComparison.Ordinal) || local.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Resource names cannot contain '/' or whitespace.",
                nameof(localName));
        }

        return $"{principal.Value:N}.{local}";
    }

    public static bool TryParse(string instanceName, out PrincipalId principal, out string localName)
    {
        principal = default;
        localName = string.Empty;
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return false;
        }

        var separator = instanceName.IndexOf('.', StringComparison.Ordinal);
        if (separator != 32) // Guid "N" format is 32 hex chars
        {
            return false;
        }

        if (!Guid.TryParseExact(instanceName[..separator], "N", out var id) || id == Guid.Empty)
        {
            return false;
        }

        var local = instanceName[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(local))
        {
            return false;
        }

        principal = new PrincipalId(id);
        localName = local;
        return true;
    }

    public static bool OwnsInstance(PrincipalId principal, string instanceName)
        => TryParse(instanceName, out var owner, out _) && owner == principal;
}
