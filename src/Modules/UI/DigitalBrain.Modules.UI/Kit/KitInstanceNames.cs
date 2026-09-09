namespace DigitalBrain.UI;

// Kit entities (chart, image) must live under the same principal partition as the chat
// that created them, so the kernel's /kit endpoints can resolve a card back to a grain
// from the caller's principal + local name alone. Mirrors PrincipalPartition's
// "{principal:N}.{local}" scheme (see PrincipalScoped.InstanceName in DigitalBrain.Silo).
internal static class KitInstanceNames
{
    public static string Sibling(string chatInstance, string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);

        // IdentityPart.Validated only forbids '/' and whitespace in an owner value, so an
        // owner like "vlad.horbachov" can legally contain '.'. The principal-partition '.'
        // is only ever the one after the owner/name '/' split, so the search must start
        // there rather than scan the whole key — otherwise a dotted owner gets truncated.
        var afterOwner = chatInstance.LastIndexOf('/') + 1;
        var separator = chatInstance.IndexOf('.', afterOwner);
        if (separator < 0)
        {
            throw new ArgumentException(
                $"'{chatInstance}' has no principal-scoped local name after its owner segment.",
                nameof(chatInstance));
        }

        return $"{chatInstance[..separator]}.{localName.Trim()}";
    }
}
