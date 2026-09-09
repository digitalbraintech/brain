namespace DigitalBrain.Excel;

internal static class ExcelKitNames
{
    public static string Sibling(string chatInstance, string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);

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
