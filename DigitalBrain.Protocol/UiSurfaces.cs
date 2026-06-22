namespace DigitalBrain.Protocol;

/// <summary>
/// Base for dynamic UI surfaces returned by installed INO experiences.
/// Clients (Flutter via sdk/, future Telegram, etc.) render these instead of hard-coded UI.
/// </summary>
[GenerateSerializer]
public record UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props) : Synapse(nameof(UiSurface), DateTimeOffset.UtcNow);

/// <summary>
/// Auth button surface. GmailDigest etc. return this so the UI kit knows to show Google icon + wire OAuth.
/// </summary>
[GenerateSerializer]
public record AuthButtonSurface(
    string Provider,
    string Label,
    string Icon = "default",
    string Action = "oauth"
) : UiSurface("auth-button", new Dictionary<string, object?>
{
    ["provider"] = Provider,
    ["label"] = Label,
    ["icon"] = Icon,
    ["action"] = Action
});

/// <summary>
/// Simple list surface for tasks / marketplace items etc.
/// </summary>
[GenerateSerializer]
public record ListSurface(
    string Title,
    IReadOnlyList<string> Items
) : UiSurface("list", new Dictionary<string, object?>
{
    ["title"] = Title,
    ["items"] = Items
});

/// <summary>
/// IDE / code edit surface for live INO modification + execute.
/// </summary>
[GenerateSerializer]
public record IdeSurface(
    string Title,
    string InitialCode,
    string Language = "ino"
) : UiSurface("ide", new Dictionary<string, object?>
{
    ["title"] = Title,
    ["code"] = InitialCode,
    ["language"] = Language
});
