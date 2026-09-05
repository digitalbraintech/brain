using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubOAuthConfiguration(IConfiguration configuration)
{
    internal const string Root = "DigitalBrain:Microsoft:GitHub:App";
    internal string ClientId => configuration[$"{Root}:ClientId"] ?? "";
    internal string ClientSecret => configuration[$"{Root}:ClientSecret"] ?? "";
    internal long AppId => long.TryParse(configuration[$"{Root}:AppId"], out var id) ? id : 0;
    internal string Slug => configuration[$"{Root}:Slug"] ?? "";
    internal Uri? PublicOrigin => Uri.TryCreate(configuration[$"{Root}:PublicOrigin"], UriKind.Absolute, out var origin)
        && (origin.Scheme == "https" || origin.Scheme == "http" && origin.IsLoopback)
        && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0 && origin.UserInfo.Length == 0
        ? origin : null;
    internal bool IsConfigured => AppId > 0 && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret) && PublicOrigin is not null;
}
